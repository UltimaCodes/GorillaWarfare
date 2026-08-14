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

    PhotonView PV;
    PlayerController owner;

    float nextShotTime;
    int shotsInBurst;          // where we are in the recoil pattern
    float lastShotTime;
    bool reloading;

    public int Ammo { get; private set; } = -1;
    public bool Reloading => reloading;

    // Reused so we're not allocating an array every shot.
    static readonly Collider[] impactColliders = new Collider[4];

    GunInfo Info => itemInfo as GunInfo;

    private void Awake()
    {
        PV = GetComponent<PhotonView>();
        owner = GetComponentInParent<PlayerController>();

        if (Info != null)
            Ammo = Info.magazineSize;
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
        if (cam == null || PV == null || Info == null || reloading)
            return;

        if (Time.time < nextShotTime)
            return;

        if (Ammo == 0)
        {
            Reload();
            return;
        }

        nextShotTime = Time.time + Info.SecondsBetweenShots;
        lastShotTime = Time.time;

        if (Ammo > 0)
            Ammo--;

        Shoot();

        // Recoil after the shot is traced, so the first round of a spray goes exactly where the
        // crosshair was rather than where the kick has already moved it.
        if (owner != null)
            owner.AddRecoil(Info.RecoilForShot(shotsInBurst), Info.recoilRecovery, Info.recoverySpeed);

        shotsInBurst++;
    }

    void Shoot()
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

        // Triggers ignored so the shot isn't eaten by volumes attached to the player.
        if (!Physics.Raycast(ray, out RaycastHit hit, Info.maxRange, ~0, QueryTriggerInteraction.Ignore))
            return;

        // InParent because colliders sit on child objects, not the root.
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

        if (damageable != null && !IsOwnedByShooter(hit.collider))
            damageable.TakeDamage(Info.damage);

        PV.RPC(nameof(RPC_Shoot), RpcTarget.All, hit.point, hit.normal);
    }

    bool IsOwnedByShooter(Collider other)
    {
        PhotonView hitView = other.GetComponentInParent<PhotonView>();
        return hitView != null && hitView.Owner == PV.Owner;
    }

    [PunRPC]
    void RPC_Shoot(Vector3 hitPosition, Vector3 hitNormal)
    {
        // This RPC already goes to everyone, so the audio is networked for free. Bank comes from
        // the weapon's own name, falling back to Shoot/ if it has nothing of its own.
        GameAudio.PlayAt($"{GameAudio.Shoot}/{gameObject.name}", transform.position, 0.6f);
        GameAudio.PlayAt(GameAudio.Impact, hitPosition, 0.5f);

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
