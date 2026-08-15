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

    float nextShotTime;
    int shotsInBurst;          // where we are in the recoil pattern
    float lastShotTime;
    bool reloading;

    public int Ammo { get; private set; } = -1;
    public bool Reloading => reloading;

    // Reused so we're not allocating an array every shot.
    static readonly Collider[] impactColliders = new Collider[4];

    GunInfo Info => itemInfo as GunInfo;

    /// Set up a weapon built at runtime by WeaponLoadout. Safe to call before Awake.
    public void Configure(GunInfo info, Camera camera, GameObject impactPrefab)
    {
        itemInfo = info;
        cam = camera;
        bulletImpactPrefab = impactPrefab;
        itemGameObject = gameObject;
        Ammo = info != null ? info.magazineSize : 0;
    }

    private void Awake()
    {
        owner = GetComponentInParent<PlayerController>();

        if (cam == null && owner != null)
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

        Material mat = Resources.Load<Material>($"Models/Weapons/Banana{gameObject.name}Mat");
        if (mat != null)
        {
            foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }
    }

    void Update()
    {
        // The spray resets once you've been off the trigger long enough, which is what lets you
        // tap-fire accurately instead of inheriting the last burst's climb.
        if (Time.time - lastShotTime > 0.35f)
            shotsInBurst = 0;
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
        if (reloading || Info == null || Ammo >= Info.magazineSize)
            return;

        StartCoroutine(ReloadRoutine());
    }

    IEnumerator ReloadRoutine()
    {
        reloading = true;
        yield return new WaitForSeconds(Info.reloadTime);
        Ammo = Info.magazineSize;
        reloading = false;
    }

    void TryShoot()
    {
        if (cam == null || owner == null || Info == null || reloading)
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
            Ammo--;

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
        int mask = ~(1 << LayerMask.NameToLayer(Hitbox.PlayerLayerName));

        if (!Physics.Raycast(ray, out RaycastHit hit, Info.maxRange, mask, QueryTriggerInteraction.Ignore))
            return false;

        if (!IsOwnedByShooter(hit.collider))
        {
            float damage = Info.DamageAtRange(hit.distance);

            Hitbox box = hit.collider.GetComponent<Hitbox>();
            if (box != null)
                box.Apply(damage);
            else
                hit.collider.GetComponentInParent<IDamageable>()?.TakeDamage(damage);
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

        if (bulletImpactPrefab == null)
            return;

        int count = Physics.OverlapSphereNonAlloc(hitPosition, 0.3f, impactColliders);
        if (count == 0)
            return;

        GameObject bulletImpactObj = Instantiate(
            bulletImpactPrefab,
            hitPosition + hitNormal * 0.001f,
            Quaternion.LookRotation(hitNormal, Vector3.up) * bulletImpactPrefab.transform.rotation);

        if (impactColliders[0] != null)
            bulletImpactObj.transform.SetParent(impactColliders[0].transform);

        Destroy(bulletImpactObj, 10f);
    }
}
