using UnityEngine;

public abstract class Gun : Item
{
    public GameObject bulletImpactPrefab;

    public abstract override void Use();
}
