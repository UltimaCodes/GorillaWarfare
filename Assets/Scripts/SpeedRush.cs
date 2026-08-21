using UnityEngine;

/// <summary>
/// What going fast looks and sounds like.
///
/// The movement is Quake-style and you can genuinely reach two or three times walking pace off a
/// pineapple, but nothing about the screen ever said so - the same view at 8 m/s and at 22 m/s,
/// which wastes the best thing the movement does.
///
/// Three things, all driven off one number. The view widens, wind lines streak past, and the
/// music ducks out of the way.
///
/// The threshold matters more than any of them. Walking must do nothing at all: an effect that
/// fires while you cross a room is not a reward for going fast, it is a permanent distortion
/// that people turn off. Nothing here starts until you are moving faster than you can run.
/// </summary>
public class SpeedRush : MonoBehaviour
{
    [Tooltip("Below this you are running, and nothing happens. Ground speed is 8.13, so this "
             + "sits above anything you can reach on foot. Lowered from 15 on 2026-08-21: a "
             + "slide peaked around 9.6 before that day's retune, which never crossed even the "
             + "old threshold, so the effect could not fire off a slide at all. 11 still clears "
             + "running comfortably while sitting inside what a real slide or chain now reaches.")]
    [SerializeField] float threshold = 11f;

    [Tooltip("Speed at which the effect is at full strength.")]
    [SerializeField] float full = 32f;

    [Tooltip("Degrees of extra field of view at full speed.")]
    [SerializeField] float fovKick = 14f;

    [Tooltip("How quickly the view catches up. Slow enough to feel like acceleration rather "
             + "than a switch being flipped.")]
    [SerializeField] float ease = 5f;

    [Tooltip("Streaks on screen per second at full speed.")]
    [SerializeField] float linesPerSecond = 22f;

    [Tooltip("How far the music ducks at full speed, as a fraction of its normal level.")]
    [SerializeField] float musicDuck = 0.45f;

    static Sprite[] streaks;

    PlayerController player;
    PlayerMovement movement;
    float rush;
    float lineDebt;

    /// How hard the player is currently going, 0 to 1. Read by MusicPlayer, which ducks rather
    /// than being told to - one number, several listeners.
    public static float Intensity { get; private set; }

    void Awake()
    {
        player = GetComponent<PlayerController>();
        movement = GetComponent<PlayerMovement>();
    }

    void Start()
    {
        // Remembered once, because the lean writes localPosition every frame and needs
        // something to return to that is not wherever it last left it.
        if (PlayerController.LocalCamera != null)
            baseCameraLocal = PlayerController.LocalCamera.transform.localPosition;
    }

    void OnDestroy()
    {
        // The player is destroyed on death and rebuilt on respawn. Leaving this behind means the
        // music stays ducked while you are staring at a killcam.
        Intensity = 0f;
    }

    void Update()
    {
        if (player == null || movement == null || !player.View.IsMine)
            return;

        // Horizontal only. Falling is not going fast, and a long drop would otherwise light up
        // the whole effect on the way down.
        Vector3 flat = movement.Velocity;
        flat.y = 0f;

        float speed = flat.magnitude;
        float wanted = Mathf.Clamp01((speed - threshold) / Mathf.Max(0.01f, full - threshold));

        rush = Mathf.MoveTowards(rush, wanted, Time.deltaTime * ease);
        Intensity = rush;

        ApplyFov();
        ApplySlideLean();
        UpdateScrape();
        SpawnLines();
    }

    void ApplyFov()
    {
        Camera camera = PlayerController.LocalCamera;

        if (camera == null || player.IsAiming)
            return;

        // Added on top of whatever the base is rather than assigned, so it stacks with the
        // player's own field of view setting instead of overwriting it.
        camera.fieldOfView = Mathf.Lerp(camera.fieldOfView,
                                        GameSettings.Fov + fovKick * rush,
                                        Time.deltaTime * ease);
    }

    /// <summary>
    /// Rolls and drops the view during a slide.
    ///
    /// A slide with no camera movement is a speed change you have to read off the screen edges.
    /// The roll is what makes it read as a body going sideways and low - the same trick every
    /// game with a slide uses, and it is doing most of the work here.
    ///
    /// Rolled about the view axis rather than the world's, so it stays a lean whichever way you
    /// happen to be looking. Eased both in and out, because a snap looks like a glitch.
    /// </summary>
    void ApplySlideLean()
    {
        Camera camera = PlayerController.LocalCamera;

        if (camera == null || movement == null)
            return;

        bool sliding = movement.Sliding;

        // Leaning toward whichever side you are actually travelling, so a slide to the left
        // rolls left. Read off velocity rather than input - you are not steering mid-slide.
        float sideways = 0f;

        if (sliding)
        {
            Vector3 flat = movement.Velocity;
            flat.y = 0f;

            if (flat.sqrMagnitude > 0.01f)
                sideways = Mathf.Clamp(Vector3.Dot(flat.normalized, camera.transform.right), -1f, 1f);
        }

        // A deeper roll the further into a chain you are, so the fourth slide looks like the
        // fourth slide rather than the first.
        float depth = sliding ? 1f + Mathf.Min(movement.SlideChain, 4) * 0.16f : 0f;

        slideRoll = Mathf.Lerp(slideRoll, sideways * slideLean * depth, Time.deltaTime * slideEase);
        slideDrop = Mathf.Lerp(slideDrop, sliding ? -slideDip : 0f, Time.deltaTime * slideEase);

        // Dust off the floor while sliding, thrown backwards along the direction of travel. This
        // and the sound are what make it read as scraping the ground rather than as gliding.
        if (sliding)
            ThrowDust();

        // Applied to the camera's local transform, on top of whatever the look angles did. The
        // holder carries the pitch; this only ever adds roll and a small drop.
        camera.transform.localRotation = Quaternion.Euler(0f, 0f, slideRoll);
        camera.transform.localPosition = baseCameraLocal + Vector3.up * slideDrop;
    }

    [Tooltip("Degrees the view rolls at full sideways speed in a slide.")]
    [SerializeField] float slideLean = 9f;

    [Tooltip("Metres the camera drops during a slide. Large on purpose - the capsule shrinking "
             + "does not move the camera at all, because the camera hangs off a fixed holder "
             + "rather than off the controller's centre. This is the entire drop, and at 0.18 it "
             + "read as ducking your head rather than as hitting the deck.")]
    [SerializeField] float slideDip = 0.95f;

    [SerializeField] float slideEase = 9f;

    float slideRoll;
    float slideDrop;
    Vector3 baseCameraLocal;

    /// <summary>
    /// Grit kicked up behind a slide.
    ///
    /// Spawned at the feet rather than at the camera, and thrown backwards, so it reads as
    /// contact with the ground - the single thing that separates a slide from floating along
    /// crouched. Rate is tied to how fast you are actually going, so a slide that has run out of
    /// speed stops throwing anything.
    /// </summary>
    void ThrowDust()
    {
        if (streaks == null || streaks.Length == 0)
        {
            streaks = Resources.LoadAll<Sprite>("Particles/Boom");

            if (streaks.Length == 0)
                return;
        }

        Vector3 flat = movement.Velocity;
        flat.y = 0f;

        float speed = flat.magnitude;

        if (speed < 2f)
            return;

        dustDebt += speed * 3.5f * Time.deltaTime;

        while (dustDebt >= 1f)
        {
            dustDebt -= 1f;

            Vector3 feet = transform.position - Vector3.up * 0.85f;
            Vector3 back = -flat.normalized;

            FlashSprite.Spawn(streaks[Random.Range(0, streaks.Length)],
                              feet + back * Random.Range(0.1f, 0.6f)
                                   + Vector3.right * Random.Range(-0.4f, 0.4f),
                              0.25f, 0.7f, 0.4f,
                              new Color(0.72f, 0.62f, 0.48f, 0.5f));
        }

    }

    /// <summary>
    /// The scrape, on a source of its own that loops.
    ///
    /// Retriggering one-shots was wrong for this. The clips are nearly two seconds long and the
    /// retrigger was every 0.16, which would have stacked eleven copies on top of each other -
    /// a sustained sound wants a sustained source, and a slide is one continuous noise rather
    /// than a series of taps.
    ///
    /// Volume and pitch both follow speed, so a slide running out is audible before it is
    /// visible - which is the cue for when to hop into the next one.
    /// </summary>
    void UpdateScrape()
    {
        bool sliding = movement != null && movement.Sliding;

        if (scrape == null)
        {
            AudioClip[] clips = Resources.LoadAll<AudioClip>("Audio/" + GameAudio.Slide);

            if (clips.Length == 0)
                return;

            scrape = gameObject.AddComponent<AudioSource>();
            scrape.clip = clips[Random.Range(0, clips.Length)];
            scrape.loop = true;
            scrape.playOnAwake = false;

            // Flat, because it is your own body. Everybody else's slide is heard through the
            // pooled positional sources like any other world sound.
            scrape.spatialBlend = 0f;
            scrape.volume = 0f;
        }

        Vector3 flat = movement != null ? movement.Velocity : Vector3.zero;
        flat.y = 0f;

        float speed = flat.magnitude;
        float wanted = sliding ? GameAudio.SlideVolume * Mathf.Clamp01(speed / 11f) : 0f;

        // Attack and release are deliberately different speeds, retuned 2026-08-21. The old
        // single rate (6/s) hit the fade-in almost instantly - nearer a click than a scrape -
        // which is what made the sound lead the camera's own, slower drop into a slide. The
        // attack reuses slideEase rather than inventing a second number for the same idea, so
        // the two are locked together instead of two constants somebody has to remember to
        // retune in step. Release is slower still, so the scrape has an audible tail instead of
        // cutting the instant `sliding` goes false - some of what read as "ends too early" was
        // really the slide itself ending early from the drag problem fixed the same day, but the
        // hard cutoff on top of that was a real, separate rough edge worth smoothing regardless.
        float rate = wanted > scrape.volume ? slideEase : 3f;

        scrape.volume = Mathf.MoveTowards(scrape.volume, wanted * GameSettings.SfxVolume,
                                          Time.deltaTime * rate);

        // Slowing down drops the pitch, which is most of what makes a scrape read as friction
        // rather than as a texture being played at you.
        scrape.pitch = 0.75f + Mathf.Clamp01(speed / 14f) * 0.5f;

        if (scrape.volume > 0.001f && !scrape.isPlaying)
            scrape.Play();
        else if (scrape.volume <= 0.001f && scrape.isPlaying)
            scrape.Stop();
    }

    float dustDebt;
    AudioSource scrape;

    void SpawnLines()
    {
        if (rush <= 0.02f)
            return;

        Camera camera = PlayerController.LocalCamera;

        if (camera == null)
            return;

        if (streaks == null || streaks.Length == 0)
        {
            streaks = Resources.LoadAll<Sprite>("Particles/Boom");

            if (streaks.Length == 0)
                return;
        }

        // Accumulated rather than one per frame, so the rate is the same at 30fps and 300.
        lineDebt += linesPerSecond * rush * Time.deltaTime;

        while (lineDebt >= 1f)
        {
            lineDebt -= 1f;

            // On a ring around the view axis, out at the edges where peripheral vision is, and
            // a few metres ahead so they streak past rather than appear on top of you.
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(1.6f, 3.2f);

            Vector3 at = camera.transform.position
                         + camera.transform.forward * Random.Range(3f, 7f)
                         + camera.transform.right * Mathf.Cos(angle) * radius
                         + camera.transform.up * Mathf.Sin(angle) * radius;

            FlashSprite.Spawn(streaks[0], at, 0.10f, 0.02f, 0.22f,
                              new Color(1f, 1f, 1f, 0.5f * rush));
        }
    }
}
