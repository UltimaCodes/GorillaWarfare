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
             + "sits above anything you can reach on foot.")]
    [SerializeField] float threshold = 15f;

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

        // Applied to the camera's local transform, on top of whatever the look angles did. The
        // holder carries the pitch; this only ever adds roll and a small drop.
        camera.transform.localRotation = Quaternion.Euler(0f, 0f, slideRoll);
        camera.transform.localPosition = baseCameraLocal + Vector3.up * slideDrop;
    }

    [Tooltip("Degrees the view rolls at full sideways speed in a slide.")]
    [SerializeField] float slideLean = 9f;

    [Tooltip("Metres the camera drops during a slide, on top of the capsule getting shorter.")]
    [SerializeField] float slideDip = 0.18f;

    [SerializeField] float slideEase = 9f;

    float slideRoll;
    float slideDrop;
    Vector3 baseCameraLocal;

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
