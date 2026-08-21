using System.Collections.Generic;
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

    /// True if this box counts as a headshot, for feedback and the kill feed.
    public bool IsHead => partName == "head" || partName == "neck";

    /// Returns true if the damage was applied.
    public bool Apply(float baseDamage, string weapon)
    {
        if (target == null)
            return false;

        target.TakeDamage(baseDamage * multiplier, weapon, IsHead);
        return true;
    }

    /// <summary>
    /// Builds a set of hitboxes onto a rig. Sphere colliders because the gorilla's limbs are
    /// short and thick - a capsule per bone would be more accurate and much fiddlier to aim at,
    /// and at this scale nobody would feel the difference.
    /// </summary>
    /// <summary>
    /// One limb or one lump of torso, described by the bones it lies between.
    ///
    /// A segment rather than a point, because that is what a body is made of. The old build put
    /// a sphere at each joint origin and nothing in between, so the upper arm, the thigh and
    /// everything below the knee had no collider at all - measured coverage was twenty percent.
    /// Four fifths of a gorilla could be shot straight through.
    /// </summary>
    struct Part
    {
        public string from;
        public string to;      // null for a lone sphere
        public float multiplier;
        public string label;
    }

    static readonly Part[] Parts =
    {
        new Part { from = "Head",          to = null,            multiplier = 2.0f, label = "head"    },
        new Part { from = "NECK",          to = "Head",          multiplier = 1.6f, label = "neck"    },
        new Part { from = "SPINE3",        to = "NECK",          multiplier = 1.0f, label = "chest"   },
        new Part { from = "SPINE1",        to = "SPINE3",        multiplier = 1.0f, label = "stomach" },
        new Part { from = "HIPS",          to = "SPINE1",        multiplier = 0.9f, label = "hips"    },
        new Part { from = "LEFTHIP",       to = "LEFTKNEE",      multiplier = 0.8f, label = "leg"     },
        new Part { from = "RIGHTHIP",      to = "RIGHTKNEE",     multiplier = 0.8f, label = "leg"     },
        new Part { from = "LEFTKNEE",      to = null,            multiplier = 0.7f, label = "leg"     },
        new Part { from = "RIGHTKNEE",     to = null,            multiplier = 0.7f, label = "leg"     },
        new Part { from = "LEFTSHOULDER",  to = "LEFTELBOW",     multiplier = 0.8f, label = "arm"     },
        new Part { from = "RIGHTSHOULDER", to = "RIGHTELBOW",    multiplier = 0.8f, label = "arm"     },
        new Part { from = "LEFTELBOW",     to = null,            multiplier = 0.7f, label = "arm"     },
        new Part { from = "RIGHTELBOW",    to = null,            multiplier = 0.7f, label = "arm"     },
    };

    /// <summary>
    /// Radius comes from a hand-edited profile rather than being fitted off the mesh.
    ///
    /// Fitting was tried first - for every sampled vertex, find the segment it sits closest to
    /// and size to a high percentile of those distances. Reasonable in principle, wrong here in
    /// practice: it trusts the mesh's own skin weights to say which part of the body a vertex
    /// belongs to, and this model's weights are painted broadly around the shoulder and hip
    /// joints - a lot of chest and back skin is dominantly weighted to the shoulder bone rather
    /// than the spine. Even a version that only compared a vertex against segments sharing its
    /// own dominant bone still measured the arm at 0.66m radius, wider than the torso. That's
    /// not a fitting bug, it's the source data - no formula run against it was going to land
    /// somewhere sane. See HitboxProfile.cs.
    /// </summary>
    static HitboxProfile profile;
    static bool profileLoaded;

    public static int BuildFor(Transform root, IDamageable owner)
    {
        int layer = LayerMask.NameToLayer(LayerName);

        if (layer < 0)
        {
            Debug.LogError($"No '{LayerName}' layer - hitboxes would be shot through.");
            return 0;
        }

        if (!profileLoaded)
        {
            profile = Resources.Load<HitboxProfile>("HitboxProfile");
            profileLoaded = true;

            if (profile == null)
            {
                Debug.LogWarning("[hitbox] no HitboxProfile in Resources - every part will use "
                                 + "the same 0.15m fallback until Assets/Resources/HitboxProfile.asset exists.");
            }
        }

        // One traversal, not one per bone. This used to walk the entire rig thirteen times for
        // every player that spawned, and respawns make that a recurring cost.
        Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            bones[t.name] = t;

        int built = 0;

        for (int i = 0; i < Parts.Length; i++)
        {
            Part part = Parts[i];

            if (!bones.TryGetValue(part.from, out Transform anchor))
                continue;

            Transform far = part.to != null && bones.TryGetValue(part.to, out Transform t2) ? t2 : null;

            GameObject go = new GameObject($"hitbox_{part.label}");
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = Vector3.zero;
            go.layer = layer;

            // The gorilla's bones carry a 100x scale from the FBX, and a child inherits it - so
            // a 0.26 radius head became a 26 metre sphere. Solid, on the Hitbox layer, which made
            // it a wall you could not walk into and could shoot someone through from across the
            // map. Cancel the bone's scale so the radii below mean metres.
            Neutralise(go.transform);

            float radius = profile != null ? profile.RadiusFor(part.from, 0.15f) : 0.15f;

            if (far == null)
            {
                SphereCollider sphere = go.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.isTrigger = false;
            }
            else
            {
                // Pointed down the segment, so the capsule lies along the limb rather than
                // across it. Rotation is set after Neutralise, which only touches scale.
                Vector3 along = far.position - anchor.position;
                float length = along.magnitude;

                if (length > 0.001f)
                    go.transform.rotation = Quaternion.LookRotation(along);

                CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
                capsule.direction = 2;                                  // local Z
                capsule.radius = radius;
                capsule.height = Mathf.Max(length + radius * 2f, radius * 2f);
                capsule.center = new Vector3(0f, 0f, length * 0.5f);
                capsule.isTrigger = false;
            }

            go.AddComponent<Hitbox>().Bind(owner, part.multiplier, part.label);
            built++;
        }

        return built;
    }

    // Everything except players and their hitboxes.
    //
    // Ground probes used to pass ~0 and hit anything at all - including the probing player's
    // own legs. That went unnoticed while hitboxes were 26 metre spheres, because a raycast
    // starting inside a convex collider reports nothing. Sizing them correctly put the knee
    // right under the ray, so every player was permanently "grounded" and got footsteps
    // mid-jump. Two bugs cancelling each other out is not the same as no bugs.
    static int worldMask;
    static bool worldMaskReady;

    public static int WorldMask
    {
        get
        {
            if (!worldMaskReady)
            {
                int player = LayerMask.NameToLayer(PlayerLayerName);
                int hitbox = LayerMask.NameToLayer(LayerName);

                worldMask = ~0;

                if (player >= 0)
                    worldMask &= ~(1 << player);

                if (hitbox >= 0)
                    worldMask &= ~(1 << hitbox);

                worldMaskReady = true;
            }

            return worldMask;
        }
    }

    /// <summary>
    /// Undoes whatever scale a parent is imposing, so this object sits at world scale 1.
    /// Bones are the reason this exists - an imported rig can carry any scale it likes, and
    /// everything hung off it silently inherits that.
    /// </summary>
    public static void Neutralise(Transform child)
    {
        Transform parent = child.parent;
        if (parent == null)
            return;

        Vector3 scale = parent.lossyScale;

        child.localScale = new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
    }

}
