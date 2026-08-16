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

    [Tooltip("How long the numbers stay up after it dies, before it stands back up.")]
    [SerializeField] float deathPause = 0.6f;

    float health;
    MonkeyRig rig;
    bool down;
    Vector3 home;

    public static TrainingDummy Build(Vector3 where, Quaternion facing, Color colour)
    {
        GameObject host = new GameObject("~Dummy");
        host.transform.SetPositionAndRotation(where, facing);

        TrainingDummy dummy = host.AddComponent<TrainingDummy>();
        dummy.home = where;

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
    public void TakeDamage(float damage, string weapon, bool headshot)
    {
        if (down)
            return;

        health -= damage;

        if (health <= 0f)
            StartCoroutine(FallOver());
    }

    IEnumerator FallOver()
    {
        down = true;

        GameAudio.PlayAt(GameAudio.Death, transform.position, GameAudio.DeathVolume);

        yield return new WaitForSeconds(deathPause);

        // Hidden rather than destroyed and rebuilt. Rebuilding a rig and thirteen hitboxes every
        // few seconds while somebody practises is a lot of garbage for no visible difference.
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = false;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            c.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        transform.position = home;
        health = maxHealth;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            r.enabled = true;

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            c.enabled = true;

        down = false;
    }
}
