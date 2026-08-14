using UnityEngine;

// Footsteps from distance travelled rather than from the movement code.
//
// Two reasons: it survives the movement rewrite untouched, and because remote players'
// transforms are already replicated, this works on every client without sending a single
// extra byte. Everyone hears everyone's steps for free.
//
// Added at runtime by PlayerController so there's no prefab wiring to lose.
public class FootstepPlayer : MonoBehaviour
{
    [SerializeField] float strideLength = 2.2f;
    [SerializeField] float volume = 0.5f;

    // Below this we treat it as standing still, so we don't tick over from jitter or from
    // the tiny corrections interpolation makes on remote players.
    const float minSpeed = 0.6f;

    Vector3 lastPosition;
    float distanceSinceStep;

    void Start()
    {
        lastPosition = transform.position;
    }

    void Update()
    {
        Vector3 position = transform.position;

        // Horizontal only - falling shouldn't sound like walking.
        Vector3 delta = position - lastPosition;
        delta.y = 0f;
        lastPosition = position;

        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        float distance = delta.magnitude;
        if (distance / dt < minSpeed)
        {
            distanceSinceStep = 0f;
            return;
        }

        distanceSinceStep += distance;
        if (distanceSinceStep < strideLength)
            return;

        distanceSinceStep = 0f;
        GameAudio.PlayAt(GameAudio.Footstep, position, volume);
    }
}
