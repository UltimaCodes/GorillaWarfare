using System.Collections;
using UnityEngine;

/// <summary>
/// A body left behind when something dies - a real player, or a training dummy.
///
/// Not a physics ragdoll - direct request was for something that reads as a body without
/// "making them properly obstacles," so this has no collider at all, on purpose. Instead the
/// rig topples over on its own (a short animated rotation, not simulated) and lies there until
/// it's cleared. A player's own body used to just vanish the instant they died
/// (`PhotonNetwork.Destroy` fired immediately); a dummy at least stayed standing, motionless,
/// for its death pause - neither one ever actually fell down.
/// </summary>
public static class Corpse
{
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
        host.AddComponent<CorpseFall>().Begin(Mathf.Max(0.4f, lifetime));
    }
}

/// The fall itself, split out from the static factory because a coroutine needs a live
/// MonoBehaviour to run on.
public class CorpseFall : MonoBehaviour
{
    public void Begin(float lifetime) => StartCoroutine(Fall(lifetime));

    IEnumerator Fall(float lifetime)
    {
        const float fallSeconds = 0.32f;

        Quaternion start = transform.rotation;

        // Topples left or right at random, rather than always the same way - a room full of the
        // exact same collapse reads as a repeating animation, not a death.
        Vector3 axis = Random.value > 0.5f ? Vector3.forward : Vector3.back;
        Quaternion end = Quaternion.AngleAxis(85f, transform.TransformDirection(axis)) * start;

        float t = 0f;

        while (t < fallSeconds)
        {
            // Unscaled - a kill's own hitstop is dragging Time.timeScale down at exactly the
            // moment this starts, and a fall that only moves on scaled time would hang mid-topple
            // for the whole freeze.
            t += Time.unscaledDeltaTime;
            float eased = 1f - (1f - Mathf.Clamp01(t / fallSeconds)) * (1f - Mathf.Clamp01(t / fallSeconds));
            transform.rotation = Quaternion.Slerp(start, end, eased);
            yield return null;
        }

        transform.rotation = end;

        yield return new WaitForSeconds(Mathf.Max(0f, lifetime - fallSeconds));

        Destroy(gameObject);
    }
}
