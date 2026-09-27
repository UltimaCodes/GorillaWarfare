using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The bar on the loading screen.
///
/// Two different things happen behind this screen and only one of them can be measured.
/// Loading the map is a real percentage - Photon reports it - but connecting to the master
/// server and joining a room is a round trip to Germany with no progress to report at all. A
/// bar that sat at zero through the second one and then jumped would be worse than no bar.
///
/// So it does both: a real fill while the level is loading, and a sweep the rest of the time.
/// The sweep is honest about what it is - it doesn't creep towards 90% pretending to know
/// something - it just moves, which is the one thing a loading screen has to do to stop looking
/// like a crash.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    [SerializeField] RectTransform track;
    [SerializeField] RectTransform fill;

    [Tooltip("How wide the sweeping block is, as a fraction of the track.")]
    [SerializeField] float sweepWidth = 0.28f;

    [Tooltip("Sweeps per second while there is nothing real to report.")]
    [SerializeField] float sweepSpeed = 0.65f;

    float sweep;

    void OnEnable()
    {
        sweep = 0f;
    }

    void Update()
    {
        if (track == null || fill == null)
            return;

        float width = track.rect.width;
        float progress = PhotonNetwork.LevelLoadingProgress;

        // Photon reports 0 when no load is running and 1 when one has finished, so only the
        // strictly-between case is a real measurement.
        bool measurable = progress > 0f && progress < 1f;

        if (measurable)
        {
            fill.anchoredPosition = new Vector2(0f, fill.anchoredPosition.y);
            fill.sizeDelta = new Vector2(width * progress, fill.sizeDelta.y);
            return;
        }

        // Unscaled, because a loading screen that stops moving when something sets the time
        // scale is the exact impression this is meant to avoid.
        sweep += Time.unscaledDeltaTime * sweepSpeed;

        if (sweep > 1f)
            sweep -= 1f;

        float block = width * sweepWidth;

        // Travels the full width plus its own length, so it enters and leaves cleanly at both
        // ends rather than appearing and vanishing at the edges.
        float travel = Mathf.Lerp(-block, width, sweep);

        // Clipped against the track rather than allowed to hang off the ends.
        float left = Mathf.Max(0f, travel);
        float right = Mathf.Min(width, travel + block);

        fill.anchoredPosition = new Vector2(left, fill.anchoredPosition.y);
        fill.sizeDelta = new Vector2(Mathf.Max(0f, right - left), fill.sizeDelta.y);
    }
}
