using UnityEngine;

/// <summary>
/// Hitstop and screenshake — the physical half of landing a shot.
///
/// The thing ULTRAKILL's parry gets right isn't the sound, it's that the game *stops* for a
/// few frames when you connect. That pause is what makes the hit feel like it had mass: your
/// input caused the world to stutter. A sound alone is a notification; a sound plus a freeze is
/// an impact. Every hit here gets a small one and a kill gets a large one, so the feedback
/// scales with how much you should care.
///
/// Two rules this has to obey, both learned the boring way:
///
///   * Nothing may run on scaled time. The whole point is that Time.timeScale is being dragged
///     down to near zero, so anything measuring itself with deltaTime freezes along with the
///     game and never restores it.
///   * The shake moves the Camera, not the CameraHolder. Look() writes CameraHolder's rotation
///     every frame, so shaking that would be a fight the shake loses - and would drag your aim
///     around, which is the difference between feedback and being punished for hitting someone.
/// </summary>
public class Juice : MonoBehaviour
{
    // Deliberately not zero. A full freeze reads as a hitch or a dropped frame; a heavy slow
    // reads as impact, because things are still visibly moving.
    //
    // Both retuned 2026-08-22 - reported outright as "doesn't work", and the doc comment above
    // already promises the ULTRAKILL freeze this wasn't delivering. There was no competing writer
    // fighting it (checked - Juice is the only thing that ever sets Time.timeScale); the numbers
    // were just too small to read as a freeze at all. 110ms at 6% speed on a kill is under a
    // tenth of a second of *real* time - closer to a flicker than a beat. Kills now get roughly
    // 2.5x the hold and a slightly harder stop; body shots and headshots scale off the same
    // strength as before and only get proportionally longer, so a stray pellet still barely
    // stutters.
    const float stopScale = 0.045f;

    const float maxStopSeconds = 0.26f;

    // Retuned 2026-08-22 - reported as not noticeable at all. 0.085m of pure camera *position*
    // was the whole effect, and a few centimetres of lateral drift barely reads on screen at a
    // normal FOV - there's nothing nearby for the eye to measure it against. Roughly doubled the
    // position and added a rotational component, which is doing most of the new work: a couple
    // of degrees of roll and pitch reads as the camera being knocked, where the same magnitude
    // in position alone reads as nothing.
    const float maxShake = 0.16f;
    const float maxShakeDegrees = 3.5f;
    const float shakeFalloff = 6.5f;
    const float shakeSpeed = 42f;

    static Juice instance;

    float stopUntil;
    float shake;
    float seed;

    /// <summary>
    /// The current shake, normalised to 0-1. Added 2026-08-22 so the HUD can shake with the
    /// world instead of sitting dead still while the camera is visibly getting knocked around it -
    /// a screen-space overlay canvas doesn't move with the camera at all, so without this the UI
    /// was the one thing on screen a hit never touched. Read-only and normalised rather than
    /// handing out the raw metre value, so a HUD reading this doesn't need to know or care what
    /// `maxShake` currently is.
    /// </summary>
    public static float Amount => instance != null ? Mathf.Clamp01(instance.shake / maxShake) : 0f;

    Camera held;
    Vector3 restPosition;
    Quaternion restRotation;

    static Juice Instance
    {
        get
        {
            if (instance != null)
                return instance;

            // Built on demand rather than placed, same as everything else here.
            GameObject host = new GameObject("~Juice");
            DontDestroyOnLoad(host);

            instance = host.AddComponent<Juice>();
            instance.seed = Random.Range(0f, 100f);

            return instance;
        }
    }

    /// <param name="strength">
    /// 0 to 1. A body shot is about a third, a headshot most of the way, a kill all of it.
    /// </param>
    public static void Hit(float strength)
    {
        strength = Mathf.Clamp01(strength);

        Juice j = Instance;

        // Unscaled, because scaled time is what's being stopped.
        float until = Time.unscaledTime + maxStopSeconds * strength;

        // Longest wins rather than adding up, so a shotgun landing nine pellets in one frame
        // doesn't stop the game for a second.
        if (until > j.stopUntil)
            j.stopUntil = until;

        j.shake = Mathf.Max(j.shake, maxShake * strength);
    }

    /// A shake with no stop, for firing. Feeling the gun go off shouldn't cost you frames.
    public static void Shake(float strength)
    {
        Juice j = Instance;
        j.shake = Mathf.Max(j.shake, maxShake * Mathf.Clamp01(strength) * 0.5f);
    }

    void LateUpdate()
    {
        bool stopped = Time.unscaledTime < stopUntil;
        Time.timeScale = stopped ? stopScale : 1f;

        // Physics has to follow, or a stopped game still moves people around at full speed.
        Time.fixedDeltaTime = 0.02f * Time.timeScale;

        ApplyShake();
    }

    void ApplyShake()
    {
        Camera camera = PlayerController.LocalCamera;

        // Respawning builds a new camera, so the rest position has to be picked up again.
        if (camera != held)
        {
            if (held != null)
            {
                held.transform.localPosition = restPosition;
                held.transform.localRotation = restRotation;
            }

            held = camera;
            restPosition = camera != null ? camera.transform.localPosition : Vector3.zero;
            restRotation = camera != null ? camera.transform.localRotation : Quaternion.identity;
        }

        if (camera == null)
            return;

        if (shake <= 0.0001f)
        {
            camera.transform.localPosition = restPosition;
            camera.transform.localRotation = restRotation;
            return;
        }

        // Perlin rather than Random, so it swings rather than jittering - noise looks like
        // static, and static reads as a broken renderer instead of a punch.
        float time = Time.unscaledTime * shakeSpeed;

        Vector3 offset = new Vector3(
            (Mathf.PerlinNoise(seed, time) - 0.5f) * 2f,
            (Mathf.PerlinNoise(seed + 11f, time) - 0.5f) * 2f,
            0f) * shake;

        camera.transform.localPosition = restPosition + offset;

        // A separate pair of noise channels so the roll and pitch don't move in lockstep with
        // the position and end up reading as one wobble instead of a knock. Normalised against
        // maxShake first since `shake` is carried in the position's units (metres), not degrees.
        float normalised = shake / maxShake;

        float roll = (Mathf.PerlinNoise(seed + 23f, time) - 0.5f) * 2f * normalised * maxShakeDegrees;
        float pitch = (Mathf.PerlinNoise(seed + 37f, time) - 0.5f) * 2f * normalised * maxShakeDegrees * 0.6f;

        camera.transform.localRotation = restRotation * Quaternion.Euler(pitch, 0f, roll);

        // Unscaled again: during hitstop, scaled time is barely advancing, and a shake that
        // decays on scaled time would hang there for the whole freeze.
        shake = Mathf.Lerp(shake, 0f, 1f - Mathf.Exp(-shakeFalloff * Time.unscaledDeltaTime));
    }

    void OnDestroy()
    {
        // Never leave the game stopped because this object went away mid-freeze.
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;

        if (instance == this)
            instance = null;
    }
}
