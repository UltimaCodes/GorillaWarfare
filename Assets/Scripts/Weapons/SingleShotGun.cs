using System.Collections;
using UnityEngine;
using Photon.Pun;

// Hitscan gun. Handles semi and full auto, fire rate, ammo, reloading and recoil.
//
// Named SingleShotGun for historical reasons - it only did one shot per click when it was
// written. Everything it needs now comes from its GunInfo, so a weapon's character is data
// rather than a subclass.
public class SingleShotGun : Item
{
    [SerializeField] Camera cam;

    PlayerController owner;
    PlayerMovement ownerMovement;
    MuzzleFlash muzzle;
    Renderer[] visualRenderers;
    MaterialPropertyBlock block;

    float nextShotTime;
    int shotsInBurst;          // where we are in the recoil pattern
    float lastShotTime;
    bool reloading;
    float reloadDoneAt;

    // ---- Purple Haze's spin, Red Hot Chili Pepper's stream ----
    //
    // Which triggers are held arrives from PlayerController every frame (Hold), because the spin
    // needs to know about holding aim as well as fire - and a weapon's own Use/UseHeld only ever
    // hear about the trigger.
    bool triggerHeld;
    bool aimHeld;
    float spin;
    float burstStartedAt;
    float nextAirblast;
    float nextFlameFeedback;
    float flameDamageShown;
    float tipLength = 0.35f;

    /// 0 to 1: how spun up the barrel is. Only moves on a weapon with a spin-up.
    public float Spin => spin;

    /// Whether the flame is going right now - replicated so everyone else sees the stream.
    public bool Flaming { get; private set; }

    /// Where the muzzle, or the nozzle, is in the world.
    public Vector3 TipPosition => muzzle != null && muzzle.Tip != null ? muzzle.Tip.position
                                : visualRoot != null ? visualRoot.TransformPoint(Vector3.forward * tipLength)
                                : transform.position;

    /// <summary>
    /// Where the nozzle looks like it is, for something the world camera draws coming out of it.
    ///
    /// Your own weapon is drawn by the second camera at a narrower field of view (ViewModelCamera),
    /// so on screen it sits lower and further out than where it really is - fire started at the
    /// real tip came out of a point in front of your face, dead centre, nowhere near the chili.
    /// This is the point the world camera draws where the second camera draws the tip: the same
    /// place on screen at the same depth. Both cameras share one position, so that's exact.
    /// Anyone else's weapon is drawn by the world camera already, and is just TipPosition.
    /// </summary>
    public Vector3 DrawnTipPosition
    {
        get
        {
            Vector3 tip = TipPosition;
            if (!owned || cam == null)
                return tip;

            if (viewModel == null)
                viewModel = cam.GetComponent<ViewModelCamera>();

            Camera drawn = viewModel != null ? viewModel.WeaponCamera : null;
            return drawn != null ? cam.ViewportToWorldPoint(drawn.WorldToViewportPoint(tip)) : tip;
        }
    }

    ViewModelCamera viewModel;

    /// Just the flash - for a grape, which has no tracer and leaves no mark where it lands.
    public void FlashMuzzle()
    {
        if (muzzle != null && Info != null && Info.muzzleFlash)
            muzzle.Fire();
    }

    /// Told every frame which triggers are down. See the fields above.
    public void Hold(bool fire, bool aim)
    {
        triggerHeld |= fire;
        aimHeld |= aim;
    }

    // False on the copies of a player that other people see. Those need the model so you can
    // tell what someone is holding, but they must never trace a shot - the owner already did,
    // and a second trace from a replicated transform would be a phantom hit.
    bool owned = true;

    public int Ammo { get; private set; } = -1;
    public int SpareMagazines { get; private set; }
    public bool Reloading => reloading;

    // Shared trace buffer. 16 is plenty - a shot passes through our own hitboxes at most.
    static readonly RaycastHit[] hits = new RaycastHit[16];

    // Everything except the movement capsules. Resolved once - LayerMask.NameToLayer is a
    // string lookup and this used to run for every pellet of every shotgun blast.
    static int traceMask;
    static bool traceMaskReady;

    public GunInfo Info => itemInfo as GunInfo;

    /// <summary>
    /// Set up a weapon built at runtime by WeaponLoadout.
    ///
    /// In practice this always runs after Awake - AddComponent on an active object runs Awake on
    /// the spot - so Awake has already built the model and the flash without knowing what the
    /// weapon is. Whatever of that needed the GunInfo is finished here: the flash sized to the
    /// weapon (Awake's Scale call had never once run for a weapon built this way), and a model
    /// that has to be turned or sized into the hand built again with the turn.
    /// </summary>
    public void Configure(GunInfo info, Camera camera, bool isOwned)
    {
        itemInfo = info;
        cam = camera;
        itemGameObject = gameObject;
        owned = isOwned;
        Ammo = info != null ? info.magazineSize : 0;
        SpareMagazines = info != null ? info.spareMagazines : 0;

        if (info == null || muzzle == null)
            return;

        if (!muzzleScaled)
        {
            muzzle.Scale(info.Weight);
            muzzleScaled = true;
        }

        if (Posed && visualRoot != null)
        {
            GameObject unposed = visualRoot.gameObject;
            if (Application.isPlaying)
                Destroy(unposed);
            else
                DestroyImmediate(unposed);

            visualRoot = null;
            muzzle.SetTipDistance(BuildVisual());

            // First, where Awake's model was - ahead of the flash Awake added after it, so
            // anything that takes a weapon's first renderer still gets the model.
            visualRoot.SetAsFirstSibling();
        }
    }

    bool muzzleScaled;

    /// A model that isn't built along +Z at gun size, the way the bananas are - see GunInfo.modelRotation.
    bool Posed => Info != null && (Info.modelRotation != Vector3.zero || !Mathf.Approximately(Info.modelScale, 1f));

    private void Awake()
    {
        owner = GetComponentInParent<PlayerController>();

        // Only ever populated for the swinger, since PlayerMovement only exists on the owner's
        // own copy - a remote copy of somebody else's peel has no CharacterController to read a
        // speed off, and does not need one, because it never computes a hit either.
        ownerMovement = owner != null ? owner.GetComponent<PlayerMovement>() : null;

        if (owned && cam == null && owner != null)
            cam = owner.GetComponentInChildren<Camera>();

        if (Info != null)
            Ammo = Info.magazineSize;

        float tip = BuildVisual();
        muzzle = gameObject.AddComponent<MuzzleFlash>();
        muzzle.SetTipDistance(tip);

        if (Info != null)
        {
            muzzle.Scale(Info.Weight);
            muzzleScaled = true;
        }
    }

    // Swaps the old AK/M1911 meshes for a banana. Done at runtime, keyed off the weapon's own
    // name, so there's no prefab surgery and adding a weapon means dropping a Banana<Name>.fbx
    // into Resources/Models/Weapons.
    /// <summary>
    /// Returns how far the muzzle end sits from the grip, so the flash lands on the tip.
    ///
    /// Public rather than called only from Awake - WeaponPreviewBaker calls this directly to
    /// build a real, correctly anchored model onto the player prefab for each weapon, so
    /// orientation (the melee hold in particular) can be judged and hand-tuned by looking
    /// directly at the model in the Scene view instead of guessing numbers and re-rendering.
    /// Awake itself doesn't run outside play mode anyway (see working-notes.md), so a baking
    /// tool was always going to need to call this piece on its own.
    /// </summary>
    public float BuildVisual()
    {
        // Weapons named after their model rather than always Banana<Name>, because not
        // everything on the roster is a banana any more.
        GameObject prefab = Resources.Load<GameObject>($"Models/Weapons/{gameObject.name}")
                            ?? Resources.Load<GameObject>($"Models/Weapons/Banana{gameObject.name}");
        if (prefab == null)
            return 0.35f;

        // Hide rather than destroy - the old meshes carry the muzzle transforms and general
        // shape the camera was framed around, and something may still reference them.
        foreach (MeshRenderer old in GetComponentsInChildren<MeshRenderer>(true))
            old.enabled = false;

        // A model that has to be turned or sized into the hand (the food kit's grapes stand up,
        // its chili lies sideways) goes inside a holder that does the anchoring, so the turn is
        // measured with it. Everything else is exactly as it always was.
        GameObject visual;
        float tip;

        if (Posed)
        {
            visual = new GameObject("~model");
            visual.transform.SetParent(transform, false);

            GameObject model = Instantiate(prefab, visual.transform);
            model.transform.localRotation = Quaternion.Euler(Info.modelRotation);
            model.transform.localScale = model.transform.localScale * Info.modelScale;

            tip = AnchorPosed(visual.transform);
        }
        else
        {
            visual = Instantiate(prefab, transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;

            // Park the model so its blunt end sits on the origin and it runs forward from there.
            //
            // The bananas are modelled about their centre, so a weapon placed at the holder had
            // half its length behind that point - fine for the pistol, but the sniper is 1.33m and
            // most of it ended up behind the camera, which is why it vanished at exactly the moment
            // it should have been most visible. Anchoring the grip means every weapon is held the
            // same way and a longer one simply reaches further out.
            tip = AnchorGrip(visual.transform);
        }

        tipLength = tip;

        Material mat = Resources.Load<Material>($"Models/Weapons/{gameObject.name}Mat")
                       ?? Resources.Load<Material>($"Models/Weapons/Banana{gameObject.name}Mat");
        if (mat != null)
        {
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }

        // No per-object outline here - ScreenOutline on the local camera already outlines the
        // held weapon along with everything else in view.
        visualRoot = visual.transform;
        reloadRestPosition = visualRoot.localPosition;

        // Melee is carried point-down from the moment it is drawn, not only while swinging.
        //
        // Remembered rather than recomputed. The swing used to multiply the karambit angle onto
        // whatever rotation it found, and the model had already been rotated once here - so the
        // pose was applied twice and every swing left the peel further round than it started.
        if (Info != null && Info.melee)
        {
            meleeHeld = visualRoot.localRotation * Quaternion.Euler(Info.meleeHold);
            visualRoot.localRotation = meleeHeld;
        }

        visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        ApplyRipeness();

        return tip;
    }

    // Bananas are built along +Z, which is also where the muzzle flash sits. Returns the
    // length, so the flash can be put on the end of whichever weapon this is.
    static float AnchorGrip(Transform visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return 0.35f;

        // Local space bounds, built from the meshes rather than Renderer.bounds - the latter is
        // world space and would fold in wherever the player happens to be standing.
        Bounds local = new Bounds();
        bool started = false;

        foreach (Renderer r in renderers)
        {
            Mesh mesh = null;

            if (r is MeshRenderer && r.TryGetComponent(out MeshFilter filter))
                mesh = filter.sharedMesh;
            else if (r is SkinnedMeshRenderer skinned)
                mesh = skinned.sharedMesh;

            if (mesh == null)
                continue;

            Bounds b = mesh.bounds;
            b.center = visual.InverseTransformPoint(r.transform.TransformPoint(b.center));

            if (!started)
            {
                local = b;
                started = true;
            }
            else
            {
                local.Encapsulate(b);
            }
        }

        if (!started)
            return 0.35f;

        visual.localPosition = new Vector3(-local.center.x, -local.center.y, -local.min.z);

        return local.size.z;
    }

    /// AnchorGrip for a model turned inside its holder: the bounds come from all eight corners of
    /// every mesh, carried through the turn - AnchorGrip moves only the centre, which is fine for a
    /// model that isn't turned and wrong the moment one is.
    static float AnchorPosed(Transform holder)
    {
        Bounds local = new Bounds();
        bool started = false;

        foreach (MeshFilter filter in holder.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1,
                                                                                 (i & 2) == 0 ? -1 : 1,
                                                                                 (i & 4) == 0 ? -1 : 1));
                Vector3 inHolder = holder.InverseTransformPoint(filter.transform.TransformPoint(corner));

                if (!started)
                {
                    local = new Bounds(inHolder, Vector3.zero);
                    started = true;
                }
                else
                {
                    local.Encapsulate(inHolder);
                }
            }
        }

        if (!started)
            return 0.35f;

        // Moves the model, not the holder, so the holder stays the weapon's own origin.
        foreach (Transform child in holder)
            child.localPosition -= new Vector3(local.center.x, local.center.y, local.min.z);

        return local.size.z;
    }

    /// <summary>
    /// Shows or hides the model without deactivating the weapon.
    ///
    /// SetActive would do it, but it also stops Update, which is what runs the reload timer -
    /// and a weapon that stops reloading while you happen to be scoped is a weapon that gets
    /// you killed. Only the renderers go.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (visualRenderers == null)
            return;

        foreach (Renderer r in visualRenderers)
        {
            if (r != null)
                r.enabled = visible;
        }

        if (muzzle != null)
            muzzle.SetVisible(visible);
    }

    /// Tints the banana by how much of the magazine is left. A property block rather than a
    /// material instance, so five weapons don't become five materials and every player doesn't
    /// get their own copy of each.
    void ApplyRipeness()
    {
        if (visualRenderers == null || Info == null || block == null)
            return;

        // Bananas only. Everything else keeps whatever colour its own material gives it - and
        // the pineapple has a texture, so tinting it green as the magazine emptied was not just
        // thematically wrong, it was painting over the artwork.
        if (!Info.ripens)
            return;

        Color c = Info.RipenessFor(Ammo);
        foreach (Renderer r in visualRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
        }
    }

    void Update()
    {
        TickReload();
        UpdateReloadFlip();

        // The spray resets once you've been off the trigger long enough, which is what lets you
        // tap-fire accurately instead of inheriting the last burst's climb.
        if (Time.time - lastShotTime > 0.35f)
            shotsInBurst = 0;

        UpdateSpinAndStream();
    }

    /// <summary>
    /// Purple Haze's barrel and Red Hot Chili Pepper's stream, from the triggers PlayerController
    /// reported this frame. The barrel winds up over `spinUp` while fire or aim is held and winds
    /// down when neither is; while it's up you move at `spinMoveMultiplier`. The stream is on while
    /// the trigger is down and there's fuel.
    /// </summary>
    void UpdateSpinAndStream()
    {
        if (Info == null)
            return;

        if (Info.spinUp > 0f)
        {
            bool winding = owned && (triggerHeld || aimHeld) && !reloading;
            spin = Mathf.MoveTowards(spin, winding ? 1f : 0f, Time.deltaTime / Info.spinUp);

            if (ownerMovement != null)
                ownerMovement.WeaponSpeedMultiplier = Mathf.Lerp(1f, Info.spinMoveMultiplier, spin);
        }

        Flaming = Info.flame && owned && triggerHeld && Ammo > 0 && !reloading;

        triggerHeld = false;
        aimHeld = false;

        UpdateFlameSound();
    }

    /// A remote copy's stream, from its owner's replicated flag - PlayerController sets it every
    /// frame, since a copy's own Flaming only ever describes a trigger it doesn't have.
    public bool RemoteFlaming { get; set; }

    const float FlameVolume = 0.55f;
    AudioSource flameLoop;

    /// <summary>
    /// The stream's roar, one looping source eased in and out rather than a sound per puff.
    ///
    /// Its own bank, Audio/Shoot/Flamer, when one is sourced. Until then the air brake's thruster
    /// burst, pitched down and looped - a short one-shot by design (see its SOURCES.txt), so the
    /// loop point is audible, but a flamethrower that makes no sound at all is worse.
    /// </summary>
    void UpdateFlameSound()
    {
        if (!Info.flame)
            return;

        bool on = owned ? Flaming : RemoteFlaming;

        if (flameLoop == null)
        {
            if (!on)
                return;

            float pitch = 1f;
            AudioClip[] clips = Resources.LoadAll<AudioClip>($"Audio/{GameAudio.Shoot}/{gameObject.name}");
            if (clips.Length == 0)
            {
                clips = Resources.LoadAll<AudioClip>($"Audio/{GameAudio.AirBrake}");
                pitch = 0.7f;
            }

            if (clips.Length == 0)
                return;

            flameLoop = gameObject.AddComponent<AudioSource>();
            flameLoop.clip = clips[Random.Range(0, clips.Length)];
            flameLoop.loop = true;
            flameLoop.playOnAwake = false;
            flameLoop.pitch = pitch;
            flameLoop.spatialBlend = 1f;
            flameLoop.minDistance = 2f;
            flameLoop.maxDistance = 40f;
            flameLoop.volume = 0f;
        }

        flameLoop.volume = Mathf.MoveTowards(flameLoop.volume, on ? FlameVolume * GameSettings.SfxVolume : 0f,
                                             Time.deltaTime * 6f);

        if (flameLoop.volume > 0.001f && !flameLoop.isPlaying)
            flameLoop.Play();
        else if (flameLoop.volume <= 0.001f && flameLoop.isPlaying)
            flameLoop.Stop();
    }

    void OnDisable()
    {
        // Stowed or gone: nothing of it should linger - not the slow, not a stream, not its roar.
        spin = 0f;
        Flaming = false;
        RemoteFlaming = false;

        if (flameLoop != null)
        {
            flameLoop.volume = 0f;
            flameLoop.Stop();
        }

        if (ownerMovement != null && Info != null && Info.spinUp > 0f)
            ownerMovement.WeaponSpeedMultiplier = 1f;
    }

    // Replaced the mild symmetric dip 2026-08-29 - reported directly as unsatisfying at any
    // speed tested, with a concrete idea for what should replace it: eat the old one, then pull
    // a fresh one out of nowhere. Bigger throw than the old version on purpose - a lean this
    // shallow read as a wobble, not a gesture.
    const float reloadTiltDegrees = 95f;
    const float reloadDropDistance = 0.18f;

    /// <summary>
    /// Three beats instead of one shape held for the whole reload. Fast down the first ~30% (down
    /// the hatch - eaten, not lowered gently), fully retracted and held through the middle (the
    /// eating), then a sharp pull back for the rest that overshoots past rest and settles - the
    /// flourish that reads as "pulled a new one out" rather than a mechanism sliding back into
    /// place. EaseOutBack's overshoot briefly pushes `amount` slightly negative, which is what
    /// produces the little backward kick past rest before it settles - not a bug, the punch.
    /// Always lands at exactly zero offset when t reaches 1 (EaseOutBack(1) == 1 exactly), same
    /// guarantee every version of this has made, so TickReload never swaps the magazine on a
    /// frame the weapon is still visibly off its mark.
    ///
    /// Skipped for melee - Reload() already refuses to start one (no ammo means Ammo is always
    /// at least magazineSize, the guard that turns Reload() into a no-op), but this still has to
    /// know not to touch visualRoot for melee even so, or it would fight meleeHeld every frame
    /// it isn't reloading rather than only the frames it is.
    /// </summary>
    void UpdateReloadFlip()
    {
        if (Info == null || Info.melee || visualRoot == null)
            return;

        if (!reloading)
        {
            visualRoot.localRotation = Quaternion.identity;
            visualRoot.localPosition = reloadRestPosition;
            return;
        }

        float elapsed = Info.reloadTime - (reloadDoneAt - Time.unscaledTime);
        float t = Info.reloadTime > 0.01f ? Mathf.Clamp01(elapsed / Info.reloadTime) : 1f;

        const float downFor = 0.3f;
        const float upFrom = 0.55f;

        float amount;
        if (t < downFor)
            amount = EaseOut(t / downFor);
        else if (t < upFrom)
            amount = 1f;
        else
            amount = 1f - EaseOutBack((t - upFrom) / (1f - upFrom));

        visualRoot.localRotation = Quaternion.Euler(reloadTiltDegrees * amount, 0f, 0f);
        visualRoot.localPosition = reloadRestPosition + Vector3.down * (reloadDropDistance * amount);
    }

    /// Reload keeps running while stowed, so switching away and back doesn't restart it. The
    /// weapon is inactive, so pull it forward from whoever is active.
    public void TickReloadWhileStowed() => TickReload();

    /// Back to a full magazine and full spares, as if just handed out. For a new round, where
    /// nobody respawns but everybody should start even.
    public void Restock()
    {
        if (Info == null)
            return;

        reloading = false;
        Ammo = Info.magazineSize;
        SpareMagazines = Info.spareMagazines;
        spin = 0f;
    }

    public override void Use()
    {
        TryShoot();
    }

    /// Called every frame the trigger is held. Automatic weapons keep firing, semi ones don't.
    public void UseHeld()
    {
        if (Info != null && Info.automatic)
            TryShoot();
    }

    public void Reload()
    {
        if (!owned || reloading || Info == null || Ammo >= Info.magazineSize)
            return;

        // No spare bananas left - you're done with this weapon until you find more.
        if (SpareMagazines <= 0)
            return;

        reloading = true;
        // Unscaled - reloading isn't blocked by anything that can also trigger hitstop (ground
        // pound, a grenade, melee all land kills independently of whichever gun is out), so a
        // reload timed on scaled time could stretch out in real time if one of those lands while
        // it's running. Same standing rule as everywhere else hitstop can reach.
        reloadDoneAt = Time.unscaledTime + Info.reloadTime;

        // Per-weapon folder first, same convention as Shoot/<WeaponName> - drop a real clip into
        // Resources/Audio/Reload/<WeaponName> and it takes over with no code change, Pick's own
        // parent-folder fallback means an empty or missing folder silently uses the shared clip
        // instead. Reported as sounding generic and identical on every weapon; every weapon
        // shares the one recording for now (nothing fruit-specific has been sourced yet), so the
        // pitch is shaped by weight in the meantime - quick and light for the pistol, heavy and
        // slow for the shotgun and sniper - rather than every reload being indistinguishable.
        float pitch = Mathf.Lerp(1.25f, 0.8f, Info.Weight);
        GameAudio.PlayShaped($"{GameAudio.Reload}/{gameObject.name}", GameAudio.ReloadVolume, pitch,
                              GameAudio.Reload, pitch);
    }

    /// Timestamp rather than a coroutine. Switching weapons deactivates the old one, which kills
    /// its coroutines - the reload never finished, so the gun stayed "reloading" forever and its
    /// magazine never refilled. It was bricked for the rest of the life.
    void TickReload()
    {
        if (!reloading || Time.unscaledTime < reloadDoneAt)
            return;

        // You ate the old one and pulled a fresh one out.
        SpareMagazines--;
        Ammo = Info.magazineSize;
        reloading = false;
        ApplyRipeness();
    }

    static int TraceMask
    {
        get
        {
            if (!traceMaskReady)
            {
                traceMask = ~(1 << LayerMask.NameToLayer(Hitbox.PlayerLayerName));
                traceMaskReady = true;
            }

            return traceMask;
        }
    }

    void TryShoot()
    {
        if (!owned || cam == null || owner == null || Info == null || reloading)
            return;

        if (Time.time < nextShotTime)
            return;

        // Not until the barrel's wound up - Purple Haze's whole price.
        if (Info.spinUp > 0f && spin < 1f)
            return;

        // Melee has no magazine to run dry.
        if (!Info.melee && Ammo == 0)
        {
            Reload();
            return;
        }

        nextShotTime = Time.time + Info.SecondsBetweenShots;
        lastShotTime = Time.time;

        // A shake with no stop. Feeling the weapon go off shouldn't cost you frames, and a
        // rifle at ten rounds a second would stutter permanently if it did.
        if (!Info.melee)
        {
            // Weight, not per-pellet damage. A shotgun pull is 108 damage across nine pellets;
            // reading `damage` here got 12 and shook a fifth as hard as the sniper.
            Juice.Shake(Info.Weight);

            if (owner != null)
                owner.AddFirePunch(Info.Weight * 0.85f);
        }

        if (!Info.melee && Ammo > 0)
        {
            Ammo--;
            ApplyRipeness();
        }

        Shoot();

        // Recoil after the shot is traced, so the first round of a spray goes exactly where the
        // crosshair was rather than where the kick has already moved it. Melee doesn't kick.
        if (owner != null && !Info.melee)
            owner.AddRecoil(Info.RecoilForShot(shotsInBurst), Info.recoilRecovery, Info.recoverySpeed, Info.viewKick);

        shotsInBurst++;
    }

    void Shoot()
    {
        // A launcher does not trace at all. Everything below - pellets, spread, falloff, the
        // endpoint the tracer draws to - describes a shot that has already arrived, and none of
        // it means anything for something that has to fly there first.
        if (Info.projectile)
        {
            ThrowShell();
            return;
        }

        if (Info.pelletProjectile)
        {
            FireGrape();
            return;
        }

        if (Info.flame)
        {
            EmitFlame();
            return;
        }

        // One trace per pellet. A shotgun is just this number going up - each pellet rolls its
        // own spread, so the group is different every shot without any extra machinery.
        int pellets = Mathf.Max(1, Info.pelletsPerShot);

        Vector3 endPoint = Vector3.zero;
        Vector3 endNormal = Vector3.zero;
        bool anyHit = false;
        bool haveEnd = false;

        for (int i = 0; i < pellets; i++)
        {
            bool hit = FirePellet(out Vector3 point, out Vector3 normal);

            // The first pellet decides what everyone sees. Eight decals and eight bangs for one
            // trigger pull would be silly, and a hit is more interesting than a miss.
            if (!haveEnd || (hit && !anyHit))
            {
                endPoint = point;
                endNormal = normal;
                anyHit = hit;
                haveEnd = true;
            }
        }

        // Reported whether or not anything was hit.
        //
        // This used to fire only on a hit, which meant a shot into the sky produced no sound,
        // no muzzle flash and no mark - nothing whatsoever. Firing into open space is most of
        // what happens in a fight, and it was the one case with no feedback at all.
        if (haveEnd)
            owner.ReportShot(gameObject.name, endPoint, endNormal, anyHit);
    }

    /// <summary>
    /// Sends a shell on its way.
    ///
    /// Launched from the camera rather than from the muzzle, so it goes exactly where the
    /// crosshair is pointing. Starting it at the end of the barrel looks more honest and means
    /// that aiming past the left edge of a doorway puts the shell into the frame - the weapon
    /// is held to the right of the view, and nobody aims with the barrel.
    ///
    /// Nudged forward far enough to clear your own body, since the sweep would otherwise hit
    /// your own hitboxes on its first frame.
    /// </summary>
    void ThrowShell()
    {
        if (owner == null || cam == null)
            return;

        Vector3 direction = cam.transform.forward;
        Vector3 origin = cam.transform.position + direction * 0.9f;

        owner.ReportProjectile(gameObject.name, origin, direction);
    }

    /// <summary>
    /// One of Purple Haze's grapes. A real thing that flies (LightProjectile), sent the way a
    /// shell is so everyone sees it - with a cone that opens up the longer the trigger stays
    /// down, so bursts beat holding it forever.
    /// </summary>
    void FireGrape()
    {
        if (owner == null || cam == null)
            return;

        if (shotsInBurst == 0)
            burstStartedAt = Time.time;

        float held = Info.spreadGrowSeconds > 0f ? Mathf.Clamp01((Time.time - burstStartedAt) / Info.spreadGrowSeconds) : 0f;
        float spread = Info.spreadMax > 0f ? Mathf.Lerp(Info.spread, Info.spreadMax, held) : Info.spread;

        Vector3 direction = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f)
                            * cam.transform.forward;

        // Off the camera, like the shell - it goes where the crosshair is - and just far enough
        // out to clear your own hitboxes.
        owner.ReportPellet(gameObject.name, cam.transform.position + direction * 0.6f, direction);
    }

    /// <summary>
    /// One puff of Red Hot Chili Pepper's stream, from the nozzle along where you're looking.
    /// Only the shooter's puffs burn anything; everyone else draws the stream from the replicated
    /// Flaming flag rather than being sent twenty-five puffs a second.
    /// </summary>
    void EmitFlame()
    {
        if (owner == null || cam == null)
            return;

        // Pressed against a wall, the nozzle is on its far side - start the fire on this side of
        // it, or it burns whoever is behind the wall.
        Vector3 from = DrawnTipPosition;
        Vector3 eye = cam.transform.position;
        if (Physics.Linecast(eye, from, out RaycastHit wall, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            from = wall.point + wall.normal * 0.05f;

        Vector3 inherited = ownerMovement != null ? ownerMovement.Velocity : Vector3.zero;
        LightProjectile.FireFlame(Info, from, cam.transform.forward, inherited, owner, this);
    }

    /// A remote copy's stream: same puffs, no burning - the shooter's own client did that.
    public void EmitFlameVisual(Vector3 direction)
    {
        if (Info != null && Info.flame)
            LightProjectile.FireFlame(Info, TipPosition, direction, Vector3.zero, owner, null);
    }

    /// <summary>
    /// TF2's airblast, on the aim key: a cone shove that throws people back - off ledges, out of
    /// the air. Costs fuel and has a short cooldown so it's a decision, not a hose.
    /// </summary>
    public void Airblast()
    {
        if (!owned || cam == null || owner == null || Info == null || Info.airblastKnockback <= 0f)
            return;

        if (reloading || Time.time < nextAirblast || Ammo < Info.airblastCost)
            return;

        nextAirblast = Time.time + Info.airblastCooldown;
        Ammo -= Info.airblastCost;

        Vector3 origin = cam.transform.position;
        Vector3 forward = cam.transform.forward;
        Vector3 shove = (forward + Vector3.up * 0.35f).normalized * Info.airblastKnockback;

        System.Collections.Generic.HashSet<PlayerController> pushed = new System.Collections.Generic.HashSet<PlayerController>();

        foreach (Collider collider in Physics.OverlapSphere(origin, Info.airblastRange,
                                                            1 << LayerMask.NameToLayer(Hitbox.LayerName),
                                                            QueryTriggerInteraction.Ignore))
        {
            PlayerController target = collider.GetComponentInParent<PlayerController>();
            if (target == null || target == owner || owner.IsTeammate(target) || pushed.Contains(target))
                continue;

            // A cone, not a sphere - it's aimed.
            Vector3 toward = target.transform.position + Vector3.up - origin;
            if (Vector3.Angle(forward, toward) > 35f)
                continue;

            pushed.Add(target);
            target.Shove(shove);
        }

        owner.ReportAirblast(TipPosition, forward);
        Juice.Shake(0.35f);
    }

    /// <summary>
    /// Traces one pellet. Returns whether it hit anything, and where the shot ended either way -
    /// on a miss that's the far end of its range, which is what the tracer needs to draw.
    /// </summary>
    bool FirePellet(out Vector3 endPoint, out Vector3 endNormal)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        ray.origin = cam.transform.position;

        float spread = Info.spread;

        // Aiming tightens the cone. Without this a scope magnifies the target and the shot
        // still lands wherever it likes, which reads as the scope being broken.
        if (owner != null && owner.IsAiming)
            spread *= Info.aimSpreadScale;

        if (spread > 0f)
        {
            // Cone around the aim direction. Deterministic recoil plus a little spread reads as
            // a weapon with character; spread alone just reads as broken.
            Vector3 dir = ray.direction;
            dir = Quaternion.Euler(Random.Range(-spread, spread),
                                   Random.Range(-spread, spread), 0f) * dir;
            ray.direction = dir;
        }

        // Everything except the Player layer: shots pass through movement capsules and land on
        // the hitboxes instead, which is what makes aiming at a head mean anything.
        int mask = TraceMask;

        // RaycastAll rather than Raycast, because the camera sits inside our own hitboxes. A
        // single raycast can stop on one of those, and then the shot goes nowhere - it just
        // silently fails to hit the wall behind it and drops a decal on our own face.
        // Where the shot ends if it hits nothing at all.
        endPoint = ray.origin + ray.direction * Info.maxRange;
        endNormal = -ray.direction;

        int count = Physics.RaycastNonAlloc(ray, hits, Info.maxRange, mask, QueryTriggerInteraction.Ignore);
        if (count == 0)
            return false;

        RaycastHit hit = default;
        float nearest = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (IsOwnedByShooter(hits[i].collider))
                continue;

            if (hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                hit = hits[i];
                found = true;
            }
        }

        if (!found)
            return false;

        endPoint = hit.point;
        endNormal = hit.normal;

        float damage = Info.DamageAtRange(hit.distance);

        // Momentum melee. Planned in ideas.md, built 2026-08-21: the peel does more the
        // faster you were travelling when it landed, so the last gun game rung is something to
        // build speed toward rather than the weapon you dread getting stuck with. Range falloff
        // above already handles distance; this is the same idea for speed instead.
        if (Info.melee && ownerMovement != null)
            damage = PlayerMovement.MomentumDamage(damage, ownerMovement.HorizontalSpeed);

        ResolveHit(hit.collider, hit.point, hit.distance, damage);
        return true;
    }

    /// <summary>
    /// What a shot does to whatever it landed on - a trace, one of Purple Haze's grapes and a puff
    /// of Red Hot Chili Pepper all come through here, so all three treat hitboxes, headshots,
    /// teammates, dummies, the style score and the hit feedback the same way.
    ///
    /// `flame` is a puff of fire: the same damage wherever it touches (fire doesn't aim, so no
    /// headshots), and none of the per-hit marker, sound, freeze or number - it touches someone
    /// twenty-five times a second, and ResolveFlameHit gives the feedback at a rate that reads.
    /// Returns whether it landed on something it could hurt (not a teammate, not a wall).
    /// </summary>
    public bool ResolveHit(Collider collider, Vector3 point, float distance, float damage, bool flame = false)
    {
        return ResolveHit(collider, point, distance, damage, flame, out _, out _);
    }

    public bool ResolveHit(Collider collider, Vector3 point, float distance, float damage, bool flame,
                           out PlayerController hitPlayer, out TrainingDummy hitDummy)
    {
        hitPlayer = null;
        hitDummy = null;

        Hitbox box = collider.GetComponent<Hitbox>();
        if (box != null)
        {
            // Your own side absorbs nothing and hears nothing. The shot still stops here - a
            // trace's FirePellet reports a hit whatever this returns, so the tracer and the impact
            // mark land where the round went, since pretending it carried on through them would be
            // a stranger lie than the friendly fire - but false tells a flame not to set them alight.
            if (IsTeammate(collider))
                return false;

            // Stashed before the hit resolves, since a kill is only confirmed several frames
            // later once the death RPC round trips - by then whether you were aiming and how
            // far away this was would already be gone. Only meaningful against another player;
            // a dummy has no actor number and no style score to feed it.
            PlayerController target = collider.GetComponentInParent<PlayerController>();
            if (target != null && target.View != null && owner != null && owner.Style != null)
            {
                owner.Style.RecordShot(target.View.Owner.ActorNumber, gameObject.name,
                                       owner.IsAiming, distance);
            }

            if (target != null)
                hitPlayer = target;
            else
            {
                TrainingDummy dummyHit = collider.GetComponentInParent<TrainingDummy>();
                if (dummyHit != null)
                    hitDummy = dummyHit;
            }

            bool head = box.IsHead && !flame;
            bool fatal = flame ? box.ApplyFlat(damage, gameObject.name) : box.Apply(damage, gameObject.name);

            // A dummy (or anything else IDamageable that isn't a player) resolves its own death
            // synchronously right here rather than several frames later over an RPC, so it can
            // never reach RegisterKill's pendingShots handshake - that only ever fires from
            // PlayerController.RPC_Died. Credit it directly instead, with the same
            // headshot/noscope/point-blank/long-range read RegisterKill would compute for a real
            // kill on this same weapon, using data that's already sitting right here.
            if (fatal && target == null && owner != null && owner.Style != null)
            {
                bool noscope = Info.canAim && !owner.IsAiming;
                bool pointBlank = distance < StyleScore.PointBlankRange;
                bool longRange = distance > StyleScore.LongRangeThreshold;
                owner.Style.RegisterDummyKill(gameObject.name, head, noscope, pointBlank, longRange);
            }
            else if (owner != null && owner.Style != null)
            {
                // Landed but didn't finish them - the multiplier starts here now rather than
                // waiting for the kill. Covers a real player (always non-fatal from this call -
                // PlayerController.TakeDamage never returns true) and a non-fatal dummy hit alike.
                owner.Style.RegisterHitLanded();
            }

            if (flame)
                return true;

            // Hit confirmation. Without this you're firing into the void and guessing.
            if (owner != null && owner.Hud != null)
                owner.Hud.ShowHit(box.IsHead);

            // 2D and named: this happened to you, and which one it was matters. It used to
            // play a generic impact, which is the same sound a shot into a wall makes - so
            // the one piece of information you most wanted was indistinguishable from missing.
            // Every hit in a row comes back a step higher, up to a point. One hit is a tick;
            // six in a row is a rising line, and the line is the part you chase.
            PlayerController.PlayHitConfirm(owner, box.IsHead);

            // The sound says you hit; the stop says it landed. A headshot gets most of the
            // budget, because the whole reason to aim at a head is that connecting should feel
            // different from connecting anywhere else.
            Juice.Hit(box.IsHead ? 0.75f : 0.3f);

            // The number, where it happened.
            if (owner != null && owner.Hud != null)
                owner.Hud.ShowDamage(point, damage * box.multiplier, box.IsHead);

            return true;
        }

        IDamageable other = collider.GetComponentInParent<IDamageable>();
        if (other == null)
            return false;

        if (other is PlayerController otherPlayer)
        {
            if (owner != null && owner.IsTeammate(otherPlayer))
                return false;

            hitPlayer = otherPlayer;
        }
        else if (other is TrainingDummy otherDummy)
        {
            hitDummy = otherDummy;
        }

        bool killed = other.TakeDamage(damage, gameObject.name, false);

        // No Hitbox here to say whether this was a headshot, but everything else that makes
        // a dummy kill invisible to RegisterKill still applies - see the box != null branch
        // above.
        if (killed && !(other is PlayerController) && owner != null && owner.Style != null)
        {
            bool noscope = Info.canAim && !owner.IsAiming;
            bool pointBlank = distance < StyleScore.PointBlankRange;
            bool longRange = distance > StyleScore.LongRangeThreshold;
            owner.Style.RegisterDummyKill(gameObject.name, false, noscope, pointBlank, longRange);
        }
        else if (owner != null && owner.Style != null)
        {
            owner.Style.RegisterHitLanded();
        }

        return true;
    }

    /// <summary>
    /// A flame puff touching someone: the hit, and setting them alight - the afterburn is what
    /// makes a flamethrower more than a short hose.
    ///
    /// The marker, the tick and the number come four times a second rather than twenty-five, the
    /// number adding up everything since the last one - and never the hit freeze a shot gets. A
    /// stream that stopped the game every time it touched someone would stutter the whole time
    /// it was on them.
    /// </summary>
    public void ResolveFlameHit(Collider collider, Vector3 point, float damage)
    {
        float distance = cam != null ? Vector3.Distance(cam.transform.position, point) : 0f;

        if (!ResolveHit(collider, point, distance, damage, true, out PlayerController player, out TrainingDummy dummy))
            return;

        flameDamageShown += damage;

        if (Time.time >= nextFlameFeedback)
        {
            nextFlameFeedback = Time.time + 0.25f;

            if (owner != null && owner.Hud != null)
            {
                owner.Hud.ShowHit(false);
                owner.Hud.ShowDamage(point, flameDamageShown, false);
            }

            PlayerController.PlayHitConfirm(owner, false);
            flameDamageShown = 0f;
        }

        if (player != null)
            player.Ignite(Info.burnSeconds, Info.burnPerSecond);
        else if (dummy != null)
            Afterburn.On(dummy.gameObject).Ignite(Info.burnSeconds, Info.burnPerSecond,
                                                  owner != null && owner.View != null ? owner.View.Owner : null);
    }

    /// <summary>
    /// Whether this collider belongs to somebody on your side.
    ///
    /// Always false outside a team mode, so deathmatch and gun game are untouched by any of
    /// this - and false for your own body too, since the void kill and any future self damage
    /// have to keep working.
    /// </summary>
    bool IsTeammate(Collider other)
    {
        if (owner == null)
            return false;

        PlayerController hitPlayer = other.GetComponentInParent<PlayerController>();
        return hitPlayer != null && owner.IsTeammate(hitPlayer);
    }

    bool IsOwnedByShooter(Collider other)
    {
        PhotonView hitView = other.GetComponentInParent<PhotonView>();
        return hitView != null && owner.View != null && hitView.Owner == owner.View.Owner;
    }

    /// <summary>
    /// The peel swing: a fast jab forward and a slower settle back.
    ///
    /// Driven by maths rather than a clip, like everything else that moves on this rig. The
    /// weapon is held point-down like a karambit, so the swing is a stab toward whatever is in
    /// front of you rather than a slash across it - and a stab reads at close range where a
    /// slash mostly leaves the screen.
    ///
    /// Unscaled time, so a kill's hitstop does not leave the arm frozen mid-jab.
    /// </summary>
    void Stab()
    {
        if (stabRoutine != null)
            StopCoroutine(stabRoutine);

        stabRoutine = StartCoroutine(StabSwing());
    }

    Coroutine stabRoutine;

    System.Collections.IEnumerator StabSwing()
    {
        Transform blade = visualRoot != null ? visualRoot : transform;

        Vector3 restPosition = blade.localPosition;

        // The pose the weapon was built with, not whatever it is currently rotated to. Reading
        // the live rotation meant an interrupted swing became the new rest, and the peel walked
        // a little further round with every stab until it was upside down.
        Quaternion held = meleeHeld;

        // Retuned 2026-08-22 - reported as "too static". The old swing was one rotation on one
        // axis, linearly interpolated, straight from rest to full extension - which is exactly
        // what a hinge does, and reads as one because there was never anywhere for the motion to
        // come *from*. Added a windup (a small pull back and twist before the stab) so the blade
        // has a start, a middle and a stop instead of a single lerp, a second axis so it reads as
        // a stab rather than a hinge swinging on rails, and eased time instead of linear so the
        // strike accelerates into contact instead of moving at one constant speed throughout.
        Quaternion windup = held * Quaternion.Euler(Info.meleeSwing * 0.22f, -9f, 0f);
        Quaternion driven = held * Quaternion.Euler(-Info.meleeSwing, 11f, 0f);

        const float windFor = 0.05f;
        const float outFor = 0.08f;
        const float backFor = 0.16f;

        const float windLunge = -0.05f;
        const float outLunge = 0.34f;

        for (float t = 0f; t < windFor; t += Time.unscaledDeltaTime)
        {
            float k = EaseOut(t / windFor);
            blade.localRotation = Quaternion.Slerp(held, windup, k);
            blade.localPosition = restPosition + Vector3.forward * (windLunge * k);
            yield return null;
        }

        for (float t = 0f; t < outFor; t += Time.unscaledDeltaTime)
        {
            float k = EaseIn(t / outFor);
            blade.localRotation = Quaternion.Slerp(windup, driven, k);
            blade.localPosition = Vector3.Lerp(restPosition + Vector3.forward * windLunge,
                                               restPosition + Vector3.forward * outLunge, k);
            yield return null;
        }

        // A small shake on contact, same as a gunshot gets - the peel is the one weapon in the
        // game that lands its hit at melee range, right in front of the camera, and had nothing
        // marking the moment at all.
        Juice.Shake(0.35f);

        for (float t = 0f; t < backFor; t += Time.unscaledDeltaTime)
        {
            float k = EaseOut(t / backFor);
            blade.localRotation = Quaternion.Slerp(driven, held, k);
            blade.localPosition = Vector3.Lerp(restPosition + Vector3.forward * outLunge, restPosition, k);
            yield return null;
        }

        blade.localRotation = held;
        blade.localPosition = restPosition;
        stabRoutine = null;
    }

    static float EaseIn(float k) => k * k;
    static float EaseOut(float k) => 1f - (1f - k) * (1f - k);

    // Standard "back" overshoot - eases to 1 but pushes slightly past it first, then settles.
    // What turns the last leg of a reload from a mechanical slide into a punch.
    static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float k1 = k - 1f;
        return 1f + c3 * k1 * k1 * k1 + c1 * k1 * k1;
    }

    Transform visualRoot;
    Vector3 reloadRestPosition;

    /// The rotation a melee weapon rests at. Fixed at build time so a swing always returns to
    /// exactly where it started rather than to wherever it happened to be interrupted.
    Quaternion meleeHeld = Quaternion.identity;

    /// Visual side of a shot. Driven from PlayerController's RPC so every client runs it, not
    /// just the shooter. Audio is played there too, since it needs the weapon's name.
    public void PlayFireEffects(Vector3 endPoint, Vector3 endNormal, bool hit)
    {
        // Not for melee, and not for anything else without a barrel. A peel has no powder and a
        // launcher throws its payload rather than firing it - flashing either made them read as
        // very short guns, which is exactly what they are not.
        if (muzzle != null && Info != null && !Info.melee && Info.muzzleFlash)
            muzzle.Fire();

        if (Info != null && Info.melee)
            Stab();

        // Melee doesn't fire anything, so a streak across the room would be a lie.
        if (Info != null && !Info.melee)
        {
            Vector3 from = muzzle != null ? muzzle.Tip.position : transform.position;

            // One streak per pellet, not one per trigger pull. The split throws nine and drew a
            // single line, so the weapon that should look like a wall of fruit looked exactly
            // like the pistol.
            //
            // Only the first is the real traced path - the others are scattered around it by
            // the weapon's own cone, which is honest about what a shotgun does without needing
            // nine separate raycasts replicated across the network.
            int streaks = Mathf.Max(1, Info.pelletsPerShot);

            BulletTracer.Spawn(from, endPoint, TracerColour());

            for (int i = 1; i < streaks; i++)
            {
                Vector3 along = endPoint - from;
                float reach = along.magnitude;

                if (reach < 0.01f)
                    break;

                // Scattered by the cone at the distance it actually travelled, so the spread
                // widens with range the way the pellets themselves do.
                Vector3 spread = Random.insideUnitCircle * Mathf.Tan(Info.spread * Mathf.Deg2Rad) * reach;
                Vector3 scattered = endPoint
                                    + Vector3.Cross(along.normalized, Vector3.up).normalized * spread.x
                                    + Vector3.up * spread.y;

                BulletTracer.Spawn(from, scattered, TracerColour());
            }
        }

        if (!hit)
            return;

        // BulletDecal re-checks locally that there is still something at the hit point before
        // it draws anything, and works out for itself whether it landed on a person or a wall.
        // Passing the trace mask so it ignores movement capsules the same way the shot did.
        BulletDecal.Spawn(endPoint, endNormal, TraceMask);
    }

    /// Each weapon already has its own ripe colour, so a shotgun blast and a sniper shot don't
    /// read as the same event. Brightened well past the fruit, because a tracer is a light.
    Color TracerColour()
    {
        Color banana = Info != null ? Info.ripe : new Color(1f, 0.85f, 0.4f);
        return Color.Lerp(banana, Color.white, 0.45f);
    }
}
