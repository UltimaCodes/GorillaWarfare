using UnityEngine;
using Photon.Pun;

public class SingleShotGun : Gun
{
    [SerializeField] Camera cam;
    [SerializeField] float maxRange = 200f;

    PhotonView PV;

    // Reused so we're not allocating an array every shot.
    static readonly Collider[] impactColliders = new Collider[4];

    private void Awake()
    {
        PV = GetComponent<PhotonView>();
    }

    public override void Use()
    {
        Shoot();
    }

    void Shoot()
    {
        if (cam == null || PV == null)
            return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        ray.origin = cam.transform.position;

        // Ignore triggers or the shot gets eaten by our own GroundCheck volume, which sits
        // right where the camera is.
        // TODO: swap the ~0 for a real layer mask once players are on their own layer.
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, ~0, QueryTriggerInteraction.Ignore))
            return;

        // InParent because the colliders are on child objects, not the root.
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

        // Camera sits inside our own capsule, so without this you can shoot yourself.
        if (damageable != null && !IsOwnedByShooter(hit.collider))
            damageable.TakeDamage(((GunInfo)itemInfo).damage);

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
        // This RPC already goes to everyone, so the audio is networked for free.
        // Bank comes from the object's own name (Rifle, Pistol...) so each weapon can have its
        // own sound without anything to wire up. Falls back to Shoot/ if it has none.
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

        // Parent it so decals stick to moving surfaces.
        if (impactColliders[0] != null)
            bulletImpactObj.transform.SetParent(impactColliders[0].transform);

        Destroy(bulletImpactObj, 10f);
    }
}
