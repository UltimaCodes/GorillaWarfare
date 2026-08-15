using UnityEngine;

// Footsteps from distance travelled rather than from the movement code, so it survives the
// movement rewrite and works on remote players too - their transforms are replicated, so
// everyone hears everyone's steps without sending anything extra.
//
// Added at runtime by PlayerController, so there's no prefab wiring.
public class FootstepPlayer : MonoBehaviour
{
    [SerializeField] float strideLength = 2.2f;
    [SerializeField] float volume = GameAudio.FootstepVolume;

    // Standing still still jitters a little, and interpolation nudges remote players about.
    const float minSpeed = 0.6f;

    // Capsule is 2 tall centred on the pivot, so the feet are 1 below. Bit of slack on top of
    // that for slopes and steps.
    const float groundProbe = 1.35f;

    Vector3 lastPosition;
    float distanceSinceStep;

    void Start()
    {
        lastPosition = transform.position;
    }

    void Update()
    {
        Vector3 position = transform.position;

        Vector3 delta = position - lastPosition;
        delta.y = 0f;
        lastPosition = position;

        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // Speed first, because it's free and the ground check is a raycast. Standing still is
        // the common case - eight players idling in a lobby were casting eight rays a frame
        // to work out that none of them had taken a step.
        float distance = delta.magnitude;
        if (distance / dt < minSpeed)
        {
            distanceSinceStep = 0f;
            return;
        }

        // Was missing entirely: you got footsteps mid-jump, because horizontal distance keeps
        // ticking over while you're airborne. Raycast rather than asking PlayerMovement,
        // because remote players don't have one - it's destroyed on non-local copies.
        if (!IsGrounded(position))
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

    bool IsGrounded(Vector3 position)
    {
        // Start slightly above the pivot so we don't begin inside the floor on a step.
        return Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, groundProbe + 0.1f,
                               Hitbox.WorldMask, QueryTriggerInteraction.Ignore);
    }
}
