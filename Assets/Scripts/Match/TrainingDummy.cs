using System.Collections;
using UnityEngine;

/// <summary>
/// Something to shoot at that shoots back at nobody.
///
/// A gorilla body with hitboxes and health and nothing else - no controller, no PhotonView, no
/// opinions. It exists so the sandbox can answer questions a moving target cannot: does the
/// headshot multiplier apply, does the shotgun fall off where it should, how far does a
/// pineapple actually throw a body.
///
/// It reuses MonkeyRig and Hitbox.BuildFor rather than approximating with a capsule, which is
/// the entire point - a dummy made of primitives would answer questions about primitives. This
/// one has the same head, the same shoulders and the same multipliers as a real player, so what
/// it tells you about a weapon is true of the weapon.
/// </summary>
public class TrainingDummy : MonoBehaviour, IDamageable
{
    [SerializeField] float maxHealth = 140f;
    [SerializeField] float respawnDelay = 2f;

    [Tooltip("Extra time added to respawnDelay before it stands back up - originally how long "
             + "a scripted fallen pose stayed up before hiding; now just more time for the "
             + "ragdoll corpse (see Corpse.Spawn in FallOver) to be worth having spawned.")]
    [SerializeField] float deathPause = 0.6f;

    float health;
    MonkeyRig rig;
    bool down;
    Vector3 home;
    Quaternion homeRotation;
    Color tint;

    public static TrainingDummy Build(Vector3 where, Quaternion facing, Color colour)
    {
        GameObject host = new GameObject("~Dummy");
        host.transform.SetPositionAndRotation(where, facing);

        TrainingDummy dummy = host.AddComponent<TrainingDummy>();
        dummy.home = where;
        dummy.homeRotation = facing;
        dummy.tint = colour;

        // The same layer players use, so weapons trace against it the same way and the ground
        // probes ignore it for the same reasons.
        int layer = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        if (layer >= 0)
            host.layer = layer;

        dummy.rig = host.AddComponent<MonkeyRig>();

        if (!dummy.rig.Build(false))
        {
            Debug.LogError("[sandbox] could not build a dummy body");
            Destroy(host);
            return null;
        }

        dummy.rig.Tint(colour);

        int boxes = Hitbox.BuildFor(host.transform, dummy);

        if (boxes == 0)
            Debug.LogError("[sandbox] dummy has no hitboxes - it cannot be shot");

        dummy.health = dummy.maxHealth;

        return dummy;
    }

    /// <summary>
    /// Same entry point players use, so a dummy is hit by exactly the code that hits a person.
    ///
    /// Anything that only worked against dummies would be worse than useless - it would be a
    /// test that passes for a weapon that does not work.
    /// </summary>
    public bool TakeDamage(float damage, string weapon, bool headshot)
    {
        if (down)
            return false;

        health -= damage;

        if (health <= 0f)
        {
            // StartCoroutine runs FallOver synchronously up to its first yield, which sets
            // `down = true` as its very first line - so even a second lethal hit landing in the
            // same frame (a shotgun's other pellets) sees `down` already set and takes the
            // early-out above instead of reporting a second fatal blow for one death.
            StartCoroutine(FallOver());
            return true;
        }

        return false;
    }

    IEnumerator FallOver()
    {
        down = true;

        GameAudio.PlayAt(GameAudio.Death, transform.position, GameAudio.DeathVolume);

        // A real physics ragdoll now, not a scripted topple - "still no source like ragdoll for
        // dummies," the same request Corpse.cs already answered for real players (see Ragdoll.cs
        // for the actual per-bone colliders and joints). This dummy hands the visual off to a
        // disposable Corpse rather than ragdolling itself: a dummy has to snap back to a clean
        // standing pose to respawn, which a settled physics simulation can't undo cleanly, so the
        // trick is to never physically touch this object at all. Corpse.Spawn builds its own
        // throwaway copy of the same rig, tinted the same colour, at this exact position and
        // facing, and that copy is the thing that falls and settles - this object just goes
        // invisible underneath it for as long as the corpse is worth having spawned.
        Corpse.Spawn(transform.position, transform.rotation, tint, deathPause + respawnDelay);

        // Hidden rather than destroyed and rebuilt. Rebuilding a rig and thirteen hitboxes every
        // few seconds while somebody practises is a lot of garbage for no visible difference.
        // Immediately, not after a pause - the corpse is what's on screen now, and a standing,
        // unhit-reacting dummy still visible next to its own ragdoll would look broken rather
        // than dead.
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        // Unscaled - a dummy's death is always accompanied by the killing weapon's own
        // Juice.Hit(), which drops Time.timeScale for a moment. A scaled wait here would let that
        // same hitstop stretch out how long the dummy stays gone, the exact hazard StyleScore and
        // SingleShotGun's own timers already call out and avoid.
        yield return new WaitForSecondsRealtime(deathPause + respawnDelay);

        transform.SetPositionAndRotation(home, homeRotation);
        health = maxHealth;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            c.enabled = true;

        down = false;
    }
}
