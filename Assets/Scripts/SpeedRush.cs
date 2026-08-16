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
    [SerializeField] float threshold = 11.5f;

    [Tooltip("Speed at which the effect is at full strength.")]
    [SerializeField] float full = 24f;

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
