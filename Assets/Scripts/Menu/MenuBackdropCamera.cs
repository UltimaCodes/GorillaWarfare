using UnityEngine;

/// <summary>
/// The camera behind the main menu: drifts slowly around wherever it was placed, so the arena
/// behind the menu reads as a live place rather than a screenshot.
///
/// Place and aim it in the scene like any camera - the drift is only ever a small wander around
/// that authored pose. Also what ShaderStack puts the post stack (the PSX filter included) on
/// while there's no player camera, so the menu looks like the game it's the menu for.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuBackdropCamera : MonoBehaviour
{
    /// The live menu camera, or null outside the menu.
    public static Camera Current { get; private set; }

    [Tooltip("How far the camera wanders from where it was placed, in metres, per axis.")]
    [SerializeField] Vector3 drift = new Vector3(0.6f, 0.15f, 0.3f);

    [Tooltip("How far it turns either way while wandering, in degrees (yaw, pitch).")]
    [SerializeField] Vector2 sway = new Vector2(2.5f, 0.8f);

    [Tooltip("Seconds for one slow wander. Long on purpose - it should be noticed, not watched.")]
    [SerializeField] float period = 26f;

    Vector3 restPosition;
    Quaternion restRotation;

    void Awake()
    {
        restPosition = transform.position;
        restRotation = transform.rotation;
    }

    void OnEnable() => Current = GetComponent<Camera>();

    void OnDisable()
    {
        if (Current == GetComponent<Camera>())
            Current = null;
    }

    void LateUpdate()
    {
        // Two incommensurate frequencies per axis, so the path never visibly repeats.
        float t = Time.unscaledTime * (Mathf.PI * 2f) / Mathf.Max(1f, period);

        Vector3 offset = new Vector3(
            Mathf.Sin(t) * drift.x,
            Mathf.Sin(t * 1.7f + 1.3f) * drift.y,
            Mathf.Sin(t * 0.6f + 2.1f) * drift.z);

        transform.position = restPosition + restRotation * offset;
        transform.rotation = restRotation * Quaternion.Euler(Mathf.Sin(t * 1.3f + 0.4f) * sway.y,
                                                             Mathf.Sin(t * 0.8f) * sway.x, 0f);
    }
}
