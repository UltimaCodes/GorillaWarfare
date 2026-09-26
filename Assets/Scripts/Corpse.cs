using UnityEngine;

/// <summary>
/// A body left behind when something dies - a real player, or a training dummy.
///
/// A real physics ragdoll now, not a scripted topple - reported directly: "its not like the
/// source engine ragdolling and I want that, the gorillas are rigged so why is that not
/// possible?" See Ragdoll.cs for the actual per-bone colliders and joints. Still not a proper
/// obstacle, per the original request that started this - the Ragdoll layer collides with the
/// world so it falls and settles on the floor, but never with Player, Hitbox or ViewModel, so a
/// body can't block a doorway or catch a shot meant for someone else.
/// </summary>
public static class Corpse
{
    static int ragdollLayer = -1;
    static bool ragdollLayerReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void PrepareRagdollLayer()
    {
        if (ragdollLayerReady)
            return;

        ragdollLayerReady = true;
        ragdollLayer = LayerMask.NameToLayer("Ragdoll");

        if (ragdollLayer < 0)
        {
            Debug.LogError("No 'Ragdoll' layer - corpses will collide with players and shots.");
            return;
        }

        int player = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        int hitbox = LayerMask.NameToLayer(Hitbox.LayerName);
        int viewModel = LayerMask.NameToLayer("ViewModel");

        if (player >= 0) Physics.IgnoreLayerCollision(ragdollLayer, player, true);
        if (hitbox >= 0) Physics.IgnoreLayerCollision(ragdollLayer, hitbox, true);
        if (viewModel >= 0) Physics.IgnoreLayerCollision(ragdollLayer, viewModel, true);

        // Bodies piling on top of each other and jittering forever is worse than two corpses
        // quietly overlapping - nobody is meant to be reading precise collision between two
        // dead bodies anyway.
        Physics.IgnoreLayerCollision(ragdollLayer, ragdollLayer, true);
    }

    public static void Spawn(Vector3 position, Quaternion facing, Color tint, float lifetime)
    {
        GameObject host = new GameObject("~Corpse");
        host.transform.SetPositionAndRotation(position, facing);

        MonkeyRig rig = host.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Object.Destroy(host);
            return;
        }

        rig.Tint(tint);

        // Built once, then removed - it exists to construct the model and cache the bind pose,
        // not to keep driving the gait every frame, which would fight the physics simulation for
        // ownership of the same bone transforms. Disabled before the (deferred) Destroy actually
        // lands, so its own Update can't sneak in one more overwrite of a bone's local rotation
        // later this same frame.
        rig.enabled = false;
        Object.Destroy(rig);

        // Mostly down and back, with enough randomness that a room full of the same death
        // doesn't collapse identically - the physics does the rest.
        Vector3 impulse = Vector3.down * 2f
                          + facing * Vector3.back * 1.5f
                          + Random.insideUnitSphere * 1.2f;

        Ragdoll.Build(host.transform, ragdollLayer, impulse);

        Object.Destroy(host, Mathf.Max(0.4f, lifetime));
    }
}
