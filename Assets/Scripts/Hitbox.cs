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
        public float fatten;   // scales the fitted radius, for parts worth being generous about
    }

    static readonly Part[] Parts =
    {
        new Part { from = "Head",          to = null,            multiplier = 2.0f, label = "head",    fatten = 1.05f },
        new Part { from = "NECK",          to = "Head",          multiplier = 1.6f, label = "neck",    fatten = 1.0f },
        new Part { from = "SPINE3",        to = "NECK",          multiplier = 1.0f, label = "chest",   fatten = 1.0f },
        new Part { from = "SPINE1",        to = "SPINE3",        multiplier = 1.0f, label = "stomach", fatten = 1.0f },
        new Part { from = "HIPS",          to = "SPINE1",        multiplier = 0.9f, label = "hips",    fatten = 1.0f },
        new Part { from = "LEFTHIP",       to = "LEFTKNEE",      multiplier = 0.8f, label = "leg",     fatten = 1.0f },
        new Part { from = "RIGHTHIP",      to = "RIGHTKNEE",     multiplier = 0.8f, label = "leg",     fatten = 1.0f },
        new Part { from = "LEFTKNEE",      to = null,            multiplier = 0.7f, label = "leg",     fatten = 1.15f },
        new Part { from = "RIGHTKNEE",     to = null,            multiplier = 0.7f, label = "leg",     fatten = 1.15f },
        new Part { from = "LEFTSHOULDER",  to = "LEFTELBOW",     multiplier = 0.8f, label = "arm",     fatten = 1.0f },
        new Part { from = "RIGHTSHOULDER", to = "RIGHTELBOW",    multiplier = 0.8f, label = "arm",     fatten = 1.0f },
        new Part { from = "LEFTELBOW",     to = null,            multiplier = 0.7f, label = "arm",     fatten = 1.15f },
        new Part { from = "RIGHTELBOW",    to = null,            multiplier = 0.7f, label = "arm",     fatten = 1.15f },
    };

    /// <summary>
    /// Radii fitted to the actual mesh, worked out once and reused.
    ///
    /// Every player is the same model, so measuring per spawn would be the same answer computed
    /// eight times. Cached against the mesh so a different model refits rather than inheriting
    /// numbers that describe a gorilla.
    /// </summary>
    static Mesh fittedFor;
    static float[] fitted;

    public static int BuildFor(Transform root, IDamageable owner)
    {
        int layer = LayerMask.NameToLayer(LayerName);

        if (layer < 0)
        {
            Debug.LogError($"No '{LayerName}' layer - hitboxes would be shot through.");
            return 0;
        }

        // One traversal, not one per bone. This used to walk the entire rig thirteen times for
        // every player that spawned, and respawns make that a recurring cost.
        Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            bones[t.name] = t;

        float[] radii = Fit(root, bones);
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

            float radius = Mathf.Max(0.04f, radii[i]);

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

    /// <summary>
    /// Works out how fat each part needs to be by looking at the mesh.
    ///
    /// For every sampled vertex, finds the segment it sits closest to and remembers how far away
    /// it was. The radius for a segment is then a high percentile of those distances - high
    /// rather than the maximum, so one stray vertex on a finger does not inflate the whole arm.
    ///
    /// Fitting beats hand-tuning here for a reason that is worth stating: the previous radii
    /// were chosen by eye and measured at twenty percent coverage. Nobody was going to spot that
    /// by looking.
    /// </summary>
    static float[] Fit(Transform root, Dictionary<string, Transform> bones)
    {
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);

        if (skin == null || skin.sharedMesh == null)
            return Fallback();

        if (fitted != null && fittedFor == skin.sharedMesh)
            return fitted;

        Mesh baked = new Mesh();
        skin.BakeMesh(baked, true);

        Vector3[] verts = baked.vertices;
        List<float>[] distances = new List<float>[Parts.Length];

        for (int i = 0; i < Parts.Length; i++)
            distances[i] = new List<float>();

        for (int v = 0; v < verts.Length; v += 3)
        {
            Vector3 world = skin.transform.TransformPoint(verts[v]);

            int best = -1;
            float bestGap = float.MaxValue;

            for (int i = 0; i < Parts.Length; i++)
            {
                if (!bones.TryGetValue(Parts[i].from, out Transform a))
                    continue;

                Vector3 pointA = a.position;
                Vector3 pointB = Parts[i].to != null && bones.TryGetValue(Parts[i].to, out Transform b)
                    ? b.position : pointA;

                float gap = Vector3.Distance(world, NearestOnSegment(pointA, pointB, world));

                if (gap >= bestGap)
                    continue;

                bestGap = gap;
                best = i;
            }

            if (best >= 0)
                distances[best].Add(bestGap);
        }

        Object.DestroyImmediate(baked);

        float[] result = new float[Parts.Length];

        for (int i = 0; i < Parts.Length; i++)
        {
            if (distances[i].Count == 0)
            {
                result[i] = 0.12f;
                continue;
            }

            distances[i].Sort();

            // Ninetieth percentile. The last ten percent is fingers, ears and the tips of toes,
            // and sizing for those would swell every capsule until they overlapped into one
            // undifferentiated blob with no head multiplier worth having.
            int at = Mathf.Clamp(Mathf.RoundToInt(distances[i].Count * 0.9f), 0, distances[i].Count - 1);
            result[i] = distances[i][at] * Parts[i].fatten;
        }

        fitted = result;
        fittedFor = skin.sharedMesh;

        return result;
    }

    static Vector3 NearestOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 along = b - a;
        float length = along.sqrMagnitude;

        if (length < 0.000001f)
            return a;

        float t = Mathf.Clamp01(Vector3.Dot(point - a, along) / length);

        return a + along * t;
    }

    /// Used when there is no mesh to measure - a stand-in, a test rig, a model that failed to
    /// load. Roughly the old hand-picked numbers, which at least produced a playable game.
    static float[] Fallback()
    {
        float[] result = new float[Parts.Length];

        for (int i = 0; i < Parts.Length; i++)
            result[i] = 0.15f;

        return result;
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
