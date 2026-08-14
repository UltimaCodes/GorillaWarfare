using UnityEngine;
using Photon.Pun;

public class SingleShotGun : Gun
{
    [SerializeField] Camera cam;
    [SerializeField] float maxRange = 200f;

    PhotonView PV;

    // Reused across shots. Physics.OverlapSphere allocates a fresh array on every call, which
    // on a fast-firing weapon is steady GC pressure for no reason.
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

        // Bounded range, and triggers ignored. Every object in this project sits on layer 0, so
        // an unbounded raycast that honoured triggers could hit the shooter's own GroundCheck
        // trigger, which is a trigger volume parented under the player and sitting right at the
        // muzzle. That consumed the shot and nothing took damage.
        if (!Physics.Raycast(ray, out RaycastHit hit, maxRange, ~0, QueryTriggerInteraction.Ignore))
            return;

        // GetComponentInParent, not GetComponent: colliders live on child objects, so a hit on
        // any body part other than the root found no IDamageable and dealt no damage.
        IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

        // Do not shoot yourself. With no layer separation the capsule collider surrounds the
        // camera, so a shot could resolve against the shooter at point-blank or against a wall
        // they were touching.
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
        if (bulletImpactPrefab == null)
            return;

        int count = Physics.OverlapSphereNonAlloc(hitPosition, 0.3f, impactColliders);
        if (count == 0)
            return;

        GameObject bulletImpactObj = Instantiate(
            bulletImpactPrefab,
            hitPosition + hitNormal * 0.001f,
            Quaternion.LookRotation(hitNormal, Vector3.up) * bulletImpactPrefab.transform.rotation);

        // Parent so the decal follows a moving surface, then destroy on a timer. SetParent must
        // happen before Destroy is scheduled so the decal cannot outlive a destroyed parent.
        if (impactColliders[0] != null)
            bulletImpactObj.transform.SetParent(impactColliders[0].transform);

        Destroy(bulletImpactObj, 10f);
    }
}
