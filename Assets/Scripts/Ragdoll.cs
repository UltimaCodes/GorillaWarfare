using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Real per-bone physics on an already-built MonkeyRig model, for Corpse to use instead of a
/// scripted topple. Reported directly: "its not like the source engine ragdolling and I want
/// that, the gorillas are rigged so why is that not possible?" It's possible because the hitbox
/// system already proved the skeleton is right there, with a from/to/radius table describing
/// every limb segment (see Hitbox.Parts) - this builds real colliders and joints along the same
/// bones instead of trigger volumes.
///
/// The Rigidbody for each bone lives directly on that bone's own Transform - the skinned mesh is
/// bound to these exact transforms, so physics has to move the real bone for the body to visibly
/// follow it, not a detached proxy. The CapsuleCollider for each bone is a fresh child instead,
/// oriented by measuring the actual rest-pose direction toward its child bone and pointing a
/// LookRotation down it - the same technique Hitbox.BuildFor already uses for the same reason:
/// which local axis runs down a bone is whatever the rig's own author picked, not a convention,
/// so it has to be measured rather than assumed.
/// </summary>
public static class Ragdoll
{
    struct Bone
    {
        public string name;
        public string parent;      // null for the root
        public string aimChild;    // which bone to measure the collider's own length/direction toward; null for a lone sphere
        public float radius;
        public float mass;
    }

    // Root first, then children after their own parent - BuildJoint always needs its parent's
    // Rigidbody already built by the time it runs.
    static readonly Bone[] Bones =
    {
        new Bone { name = "HIPS",          parent = null,            aimChild = "SPINE1",        radius = 0.24f, mass = 9f },
        new Bone { name = "SPINE1",        parent = "HIPS",          aimChild = "SPINE3",        radius = 0.24f, mass = 8f },
        new Bone { name = "SPINE3",        parent = "SPINE1",        aimChild = "NECK",          radius = 0.24f, mass = 8f },
        new Bone { name = "Head",          parent = "SPINE3",        aimChild = null,            radius = 0.16f, mass = 4f },
        new Bone { name = "LEFTHIP",       parent = "HIPS",          aimChild = "LEFTKNEE",      radius = 0.16f, mass = 5f },
        new Bone { name = "LEFTKNEE",      parent = "LEFTHIP",       aimChild = null,            radius = 0.13f, mass = 3f },
        new Bone { name = "RIGHTHIP",      parent = "HIPS",          aimChild = "RIGHTKNEE",     radius = 0.16f, mass = 5f },
        new Bone { name = "RIGHTKNEE",     parent = "RIGHTHIP",      aimChild = null,            radius = 0.13f, mass = 3f },
        new Bone { name = "LEFTSHOULDER",  parent = "SPINE3",        aimChild = "LEFTELBOW",     radius = 0.13f, mass = 3f },
        new Bone { name = "LEFTELBOW",     parent = "LEFTSHOULDER",  aimChild = null,            radius = 0.1f,  mass = 2f },
        new Bone { name = "RIGHTSHOULDER", parent = "SPINE3",        aimChild = "RIGHTELBOW",    radius = 0.13f, mass = 3f },
        new Bone { name = "RIGHTELBOW",    parent = "RIGHTSHOULDER", aimChild = null,            radius = 0.1f,  mass = 2f },
    };

    /// <summary>
    /// Builds the physics rig and gives the hips an initial shove, so the body actually falls
    /// with the death rather than going limp in place. Returns the root Rigidbody so the caller
    /// can decide when the body has settled (see Corpse.cs).
    /// </summary>
    public static Rigidbody Build(Transform root, int layer, Vector3 impulse)
    {
        Dictionary<string, Transform> transforms = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (!transforms.ContainsKey(t.name))
                transforms[t.name] = t;
        }

        Dictionary<string, Rigidbody> bodies = new Dictionary<string, Rigidbody>();
        Rigidbody hips = null;

        foreach (Bone bone in Bones)
        {
            if (!transforms.TryGetValue(bone.name, out Transform t))
                continue;

            Rigidbody body = t.gameObject.AddComponent<Rigidbody>();
            body.mass = bone.mass;

            // Damped rather than left at Unity's defaults - an undamped ragdoll this size reads
            // as weightless, drifting long after it should have settled.
            body.linearDamping = 0.5f;
            body.angularDamping = 0.5f;

            BuildCollider(t, transforms, bone, layer);
            bodies[bone.name] = body;

            if (bone.parent == null)
            {
                hips = body;
                continue;
            }

            if (!bodies.TryGetValue(bone.parent, out Rigidbody parentBody))
                continue;

            CharacterJoint joint = t.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;

            // Generic, forgiving limits rather than a per-joint anatomical tuning pass - a wide
            // tolerance still reads as a body falling loosely rather than one locked rigid, and
            // forgives not knowing this rig's own per-bone local axis convention precisely. Real
            // per-joint feel is exactly the kind of thing that needs a person watching it, the
            // same as every other pose or feel number in this project - see roadmap.md's
            // Unverified section.
            SoftJointLimit twist = new SoftJointLimit { limit = 30f };
            joint.lowTwistLimit = new SoftJointLimit { limit = -30f };
            joint.highTwistLimit = twist;
            joint.swing1Limit = new SoftJointLimit { limit = 40f };
            joint.swing2Limit = new SoftJointLimit { limit = 40f };
        }

        if (hips != null)
            hips.AddForce(impulse, ForceMode.VelocityChange);

        return hips;
    }

    static void BuildCollider(Transform bone, Dictionary<string, Transform> transforms, Bone info, int layer)
    {
        GameObject go = new GameObject($"~col_{info.name}");
        go.transform.SetParent(bone, false);
        go.layer = layer;

        // Scale first, at identity rotation - Neutralise only cancels scale correctly before any
        // rotation is applied, same ordering Hitbox.BuildFor already established.
        Hitbox.Neutralise(go.transform);

        Transform aim = info.aimChild != null && transforms.TryGetValue(info.aimChild, out Transform far)
            ? far : null;

        if (aim == null)
        {
            SphereCollider sphere = go.AddComponent<SphereCollider>();
            sphere.radius = info.radius;
            return;
        }

        // Measured from the actual rest pose rather than assumed - which local axis runs down a
        // bone is this rig's own author's choice, not a convention (see Hitbox.BuildFor's own
        // note on the same problem). World space direction avoids needing to know it at all.
        Vector3 along = aim.position - bone.position;
        float length = along.magnitude;

        if (length > 0.001f)
            go.transform.rotation = Quaternion.LookRotation(along);

        CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
        capsule.direction = 2; // local Z, pointed down the segment - same convention Hitbox uses
        capsule.radius = info.radius;
        capsule.height = Mathf.Max(length + info.radius * 2f, info.radius * 2f);
        capsule.center = new Vector3(0f, 0f, length * 0.5f);
    }
}
