using UnityEngine;

// A damage volume attached to a bone. Head hits hurt more than leg hits.
//
// These sit on the Hitbox layer while the movement capsule sits on Player, and weapons only
// trace against Hitbox. That separation is the whole trick: the CharacterController is a single
// capsule around the whole body, so if shots hit that, every part of a player is worth the same
// and there is nowhere to aim.
public class Hitbox : MonoBehaviour
{
    public const string LayerName = "Hitbox";
    public const string PlayerLayerName = "Player";

    public float multiplier = 1f;
    public string partName = "body";

    IDamageable target;

    public void Bind(IDamageable owner, float damageMultiplier, string label)
    {
        target = owner;
        multiplier = damageMultiplier;
        partName = label;
    }

    /// Returns true if the damage was applied.
    public bool Apply(float baseDamage)
    {
        if (target == null)
            return false;

        target.TakeDamage(baseDamage * multiplier);
        return true;
    }

    /// <summary>
    /// Builds a set of hitboxes onto a rig. Sphere colliders because the gorilla's limbs are
    /// short and thick - a capsule per bone would be more accurate and much fiddlier to aim at,
    /// and at this scale nobody would feel the difference.
    /// </summary>
    public static int BuildFor(Transform root, IDamageable owner)
    {
        // bone name, radius, multiplier, label
        (string bone, float radius, float mult, string label)[] parts =
        {
            ("Head",           0.17f, 2.0f, "head"),
            ("NECK",           0.12f, 1.6f, "neck"),
            ("SPINE3",         0.26f, 1.0f, "chest"),
            ("SPINE1",         0.24f, 1.0f, "stomach"),
            ("HIPS",           0.22f, 0.9f, "hips"),
            ("LEFTHIP",        0.14f, 0.8f, "leg"),
            ("RIGHTHIP",       0.14f, 0.8f, "leg"),
            ("LEFTKNEE",       0.12f, 0.7f, "leg"),
            ("RIGHTKNEE",      0.12f, 0.7f, "leg"),
            ("LEFTSHOULDER",   0.13f, 0.8f, "arm"),
            ("RIGHTSHOULDER",  0.13f, 0.8f, "arm"),
            ("LEFTELBOW",      0.11f, 0.7f, "arm"),
            ("RIGHTELBOW",     0.11f, 0.7f, "arm"),
        };

        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0)
        {
            Debug.LogError($"No '{LayerName}' layer - hitboxes would be shot through.");
            return 0;
        }

        int built = 0;
        foreach ((string bone, float radius, float mult, string label) in parts)
        {
            Transform t = FindBone(root, bone);
            if (t == null)
                continue;

            GameObject go = new GameObject($"hitbox_{label}");
            go.transform.SetParent(t, false);
            go.transform.localPosition = Vector3.zero;
            go.layer = layer;

            SphereCollider col = go.AddComponent<SphereCollider>();
            col.radius = radius;
            col.isTrigger = false;

            go.AddComponent<Hitbox>().Bind(owner, mult, label);
            built++;
        }

        return built;
    }

    static Transform FindBone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }
}
