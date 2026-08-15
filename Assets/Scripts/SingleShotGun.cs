using System.Collections;
using UnityEngine;
using Photon.Pun;

// Hitscan gun. Handles semi and full auto, fire rate, ammo, reloading and recoil.
//
// Named SingleShotGun for historical reasons - it only did one shot per click when it was
// written. Everything it needs now comes from its GunInfo, so a weapon's character is data
// rather than a subclass.
public class SingleShotGun : Gun
{
    [SerializeField] Camera cam;

    PlayerController owner;
    MuzzleFlash muzzle;
    Renderer[] visualRenderers;
    MaterialPropertyBlock block;

    float nextShotTime;
    int shotsInBurst;          // where we are in the recoil pattern
    float lastShotTime;
    bool reloading;
    float reloadDoneAt;

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

    /// Set up a weapon built at runtime by WeaponLoadout. Safe to call before Awake.
    public void Configure(GunInfo info, Camera camera, bool isOwned)
    {
        itemInfo = info;
        cam = camera;
        itemGameObject = gameObject;
        owned = isOwned;
        Ammo = info != null ? info.magazineSize : 0;
        SpareMagazines = info != null ? info.spareMagazines : 0;
    }

    private void Awake()
    {
        owner = GetComponentInParent<PlayerController>();

        if (owned && cam == null && owner != null)
            cam = owner.GetComponentInChildren<Camera>();

        if (Info != null)
            Ammo = Info.magazineSize;

        float tip = BuildVisual();
        muzzle = gameObject.AddComponent<MuzzleFlash>();
        muzzle.SetTipDistance(tip);
    }

    // Swaps the old AK/M1911 meshes for a banana. Done at runtime, keyed off the weapon's own
    // name, so there's no prefab surgery and adding a weapon means dropping a Banana<Name>.fbx
    // into Resources/Models/Weapons.
    /// Returns how far the muzzle end sits from the grip, so the flash lands on the tip.
    float BuildVisual()
    {
        GameObject prefab = Resources.Load<GameObject>($"Models/Weapons/Banana{gameObject.name}");
        if (prefab == null)
            return 0.35f;

        // Hide rather than destroy - the old meshes carry the muzzle transforms and general
        // shape the camera was framed around, and something may still reference them.
        foreach (MeshRenderer old in GetComponentsInChildren<MeshRenderer>(true))
            old.enabled = false;

        GameObject visual = Instantiate(prefab, transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // Park the model so its blunt end sits on the origin and it runs forward from there.
        //
        // The bananas are modelled about their centre, so a weapon placed at the holder had
        // half its length behind that point - fine for the pistol, but the sniper is 1.33m and
        // most of it ended up behind the camera, which is why it vanished at exactly the moment
        // it should have been most visible. Anchoring the grip means every weapon is held the
        // same way and a longer one simply reaches further out.
        float tip = AnchorGrip(visual.transform);

        Material mat = Resources.Load<Material>($"Models/Weapons/Banana{gameObject.name}Mat");
        if (mat != null)
        {
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
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
    }

    /// Tints the banana by how much of the magazine is left. A property block rather than a
    /// material instance, so five weapons don't become five materials and every player doesn't
    /// get their own copy of each.
    void ApplyRipeness()
    {
        if (visualRenderers == null || Info == null || block == null)
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

        // The spray resets once you've been off the trigger long enough, which is what lets you
        // tap-fire accurately instead of inheriting the last burst's climb.
        if (Time.time - lastShotTime > 0.35f)
            shotsInBurst = 0;
    }

    /// Reload keeps running while stowed, so switching away and back doesn't restart it. The
    /// weapon is inactive, so pull it forward from whoever is active.
    public void TickReloadWhileStowed() => TickReload();

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
        reloadDoneAt = Time.time + Info.reloadTime;

        // Was a random clip out of the UI bank, so reloading sounded like clicking a button.
        GameAudio.Play2D(GameAudio.Reload, GameAudio.ReloadVolume, 0.06f);
    }

    /// Timestamp rather than a coroutine. Switching weapons deactivates the old one, which kills
    /// its coroutines - the reload never finished, so the gun stayed "reloading" forever and its
    /// magazine never refilled. It was bricked for the rest of the life.
    void TickReload()
    {
        if (!reloading || Time.time < reloadDoneAt)
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
            Juice.Shake(Mathf.Clamp01(Info.damage / 60f));

            if (owner != null)
                owner.AddFirePunch(Mathf.Clamp01(Info.damage / 90f));
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
            owner.AddRecoil(Info.RecoilForShot(shotsInBurst), Info.recoilRecovery, Info.recoverySpeed);

        shotsInBurst++;
    }

    void Shoot()
    {
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

        Hitbox box = hit.collider.GetComponent<Hitbox>();
        if (box != null)
        {
            box.Apply(damage, gameObject.name);

            // Hit confirmation. Without this you're firing into the void and guessing.
            if (owner != null && owner.Hud != null)
                owner.Hud.ShowHit(box.IsHead);

            // 2D and named: this happened to you, and which one it was matters. It used to
            // play a generic impact, which is the same sound a shot into a wall makes - so
            // the one piece of information you most wanted was indistinguishable from missing.
            // Every hit in a row comes back a step higher, up to a point. One hit is a tick;
            // six in a row is a rising line, and the line is the part you chase.
            int hits = owner != null ? owner.RegisterHit() : 1;
            float pitch = 1f + Mathf.Min(hits - 1, 9) * 0.055f;

            GameAudio.PlayPitched(GameAudio.Hit, box.IsHead ? "headshot" : "hit",
                                  GameAudio.HitVolume, pitch);

            // The sound says you hit; the stop says it landed. A headshot gets most of the
            // budget, because the whole reason to aim at a head is that connecting should feel
            // different from connecting anywhere else.
            Juice.Hit(box.IsHead ? 0.75f : 0.3f);

            // The number, where it happened.
            if (owner != null && owner.Hud != null)
                owner.Hud.ShowDamage(hit.point, damage * box.multiplier, box.IsHead);
        }
        else
        {
            hit.collider.GetComponentInParent<IDamageable>()?.TakeDamage(damage, gameObject.name, false);
        }

        return true;
    }

    bool IsOwnedByShooter(Collider other)
    {
        PhotonView hitView = other.GetComponentInParent<PhotonView>();
        return hitView != null && owner.View != null && hitView.Owner == owner.View.Owner;
    }

    /// Visual side of a shot. Driven from PlayerController's RPC so every client runs it, not
    /// just the shooter. Audio is played there too, since it needs the weapon's name.
    public void PlayFireEffects(Vector3 endPoint, Vector3 endNormal, bool hit)
    {
        if (muzzle != null)
            muzzle.Fire();

        // Melee doesn't fire anything, so a streak across the room would be a lie.
        if (Info != null && !Info.melee)
        {
            Vector3 from = muzzle != null ? muzzle.Tip.position : transform.position;
            BulletTracer.Spawn(from, endPoint, TracerColour());
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
