using UnityEngine;

/// <summary>
/// The one thing that writes the player camera's local position and rotation.
///
/// It used to be two. SpeedRush wrote a slide dip and lean every Update; Juice wrote its screen
/// shake every LateUpdate, on top of a "rest" pose it remembered the first frame it ran. Juice is
/// only created by the first shot or hit of a session, so when that landed mid-slide the rest it
/// remembered was the dipped camera - and it put the camera back there every frame afterwards,
/// for the rest of that life. Reported as the view staying down after a slide: gun at the top of
/// the screen, looking up at everyone.
///
/// Now Juice and SpeedRush only publish offsets, and this composes them onto a rest taken in
/// Awake - the moment the camera is built, before anything has had a chance to move it. Runs
/// after every ordinary LateUpdate, so it always has this frame's numbers.
/// </summary>
[DefaultExecutionOrder(1000)]
public class CameraPose : MonoBehaviour
{
    Vector3 restPosition;
    Quaternion restRotation;

    void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
    }

    void LateUpdate()
    {
        // The holder above this carries the look pitch and the slide's drop (PlayerController);
        // everything here is on top of that and only ever returns to the rest pose it started from.
        transform.localPosition = restPosition + Juice.ShakeOffset;
        transform.localRotation = restRotation * Quaternion.Euler(0f, 0f, SpeedRush.ViewRoll) * Juice.ShakeRotation;
    }
}
