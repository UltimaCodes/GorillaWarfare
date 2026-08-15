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
    public void Configure(GunInfo info, Camera camera, GameObject impactPrefab, bool isOwned)
    {
        itemInfo = info;
        cam = camera;
        bulletImpactPrefab = impactPrefab;
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

        BuildVisual();
        muzzle = gameObject.AddComponent<MuzzleFlash>();
    }

    // Swaps the old AK/M1911 meshes for a banana. Done at runtime, keyed off the weapon's own
    // name, so there's no prefab surgery and adding a weapon means dropping a Banana<Name>.fbx
    // into Resources/Models/Weapons.
    void BuildVisual()
    {
        GameObject prefab = Resources.Load<GameObject>($"Models/Weapons/Banana{gameObject.name}");
        if (prefab == null)
            return;

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
        AnchorGrip(visual.transform);

        Material mat = Resources.Load<Material>($"Models/Weapons/Banana{gameObject.name}Mat");
        if (mat != null)
        {
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }

        visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        ApplyRipeness();
    }

    // Bananas are built along +Z, which is also where the muzzle flash sits.
    static void AnchorGrip(Transform visual)
    {
        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

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
            return;

        visual.localPosition = new Vector3(-local.center.x, -local.center.y, -local.min.z);
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
        GameAudio.Play2D(GameAudio.UI, 0.4f);
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
        bool reported = false;

        for (int i = 0; i < pellets; i++)
            reported |= FirePellet(reported);
    }

    /// Returns true if this pellet hit something. Only the first hit reports to the network -
    /// eight decals and eight bangs for one trigger pull would be silly.
    bool FirePellet(bool alreadyReported)
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        ray.origin = cam.transform.position;

        if (Info.spread > 0f)
        {
            // Cone around the aim direction. Deterministic recoil plus a little spread reads as
            // a weapon with character; spread alone just reads as broken.
            Vector3 dir = ray.direction;
            dir = Quaternion.Euler(Random.Range(-Info.spread, Info.spread),
                                   Random.Range(-Info.spread, Info.spread), 0f) * dir;
            ray.direction = dir;
        }

        // Everything except the Player layer: shots pass through movement capsules and land on
        // the hitboxes instead, which is what makes aiming at a head mean anything.
        int mask = TraceMask;

        // RaycastAll rather than Raycast, because the camera sits inside our own hitboxes. A
        // single raycast can stop on one of those, and then the shot goes nowhere - it just
        // silently fails to hit the wall behind it and drops a decal on our own face.
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

        float damage = Info.DamageAtRange(hit.distance);

        Hitbox box = hit.collider.GetComponent<Hitbox>();
        if (box != null)
        {
            box.Apply(damage, gameObject.name);

            // Hit confirmation. Without this you're firing into the void and guessing.
            if (owner != null && owner.Hud != null)
                owner.Hud.ShowHit(box.IsHead);

            GameAudio.Play2D(GameAudio.Impact, 0.35f, 0.15f);
        }
        else
        {
            hit.collider.GetComponentInParent<IDamageable>()?.TakeDamage(damage, gameObject.name, false);
        }

        if (!alreadyReported)
            owner.ReportShot(gameObject.name, hit.point, hit.normal);

        return true;
    }

    bool IsOwnedByShooter(Collider other)
    {
        PhotonView hitView = other.GetComponentInParent<PhotonView>();
        return hitView != null && owner.View != null && hitView.Owner == owner.View.Owner;
    }

    /// Visual side of a shot. Driven from PlayerController's RPC so every client runs it, not
    /// just the shooter. Audio is played there too, since it needs the weapon's name.
    public void PlayFireEffects(Vector3 hitPosition, Vector3 hitNormal)
    {
        if (muzzle != null)
            muzzle.Fire();

        // BulletDecal re-checks locally that there is still something at the hit point before
        // it draws anything, and works out for itself whether it landed on a person or a wall.
        // Passing the trace mask so it ignores movement capsules the same way the shot did.
        BulletDecal.Spawn(hitPosition, hitNormal, TraceMask);
    }
}
