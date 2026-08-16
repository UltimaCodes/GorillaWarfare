using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Watches whoever just killed you, for the three seconds you are dead anyway.
///
/// Death already had a camera - it sat above your body looking down at it, which stops death
/// being a black screen but tells you nothing. The one question you actually have when you die
/// is "where were they", and pointing the camera at the answer costs nothing because the time is
/// already being spent.
///
/// Falls back to the body cam when there is nobody to watch: you fell off the map, you shot
/// yourself, or your killer has since died or left. That fallback is the common case in a small
/// room and it has to look deliberate rather than broken.
/// </summary>
public class KillCam : MonoBehaviour
{
    [Tooltip("How far behind the killer the camera sits.")]
    [SerializeField] float distance = 3.2f;

    [Tooltip("And how far above them.")]
    [SerializeField] float height = 1.9f;

    [Tooltip("How quickly the camera settles onto its target. Low is floaty, high is snappy.")]
    [SerializeField] float follow = 6f;

    Player target;
    Vector3 restPosition;
    Quaternion restRotation;

    /// <summary>
    /// Who to watch, and where to sit if there turns out to be nobody.
    ///
    /// The rest pose is passed in rather than read off the transform, because the camera may
    /// already have drifted toward a killer who then died - and snapping back to wherever it
    /// happened to be at that moment is not a resting place.
    /// </summary>
    public void Watch(Player killer, Vector3 fallbackPosition, Quaternion fallbackRotation)
    {
        target = killer;
        restPosition = fallbackPosition;
        restRotation = fallbackRotation;

        transform.SetPositionAndRotation(fallbackPosition, fallbackRotation);
    }

    void LateUpdate()
    {
        Transform body = Find();

        if (body == null)
        {
            // Nobody to watch. Ease back rather than cut, so a killer dying mid-killcam looks
            // like the camera losing interest instead of the game glitching.
            transform.position = Vector3.Lerp(transform.position, restPosition, Time.deltaTime * follow);
            transform.rotation = Quaternion.Slerp(transform.rotation, restRotation, Time.deltaTime * follow);
            return;
        }

        // Behind and above, looking at the head rather than the feet.
        Vector3 head = body.position + Vector3.up * height;
        Vector3 wanted = head - body.forward * distance + Vector3.up * 0.6f;

        // Nothing solid between the camera and them. Without this the camera ends up inside a
        // wall about a third of the time and the killcam shows you grey.
        if (Physics.Linecast(head, wanted, out RaycastHit hit, Hitbox.WorldMask,
                             QueryTriggerInteraction.Ignore))
            wanted = hit.point + hit.normal * 0.3f;

        transform.position = Vector3.Lerp(transform.position, wanted, Time.deltaTime * follow);

        Quaternion look = Quaternion.LookRotation(head - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * follow);
    }

    /// The killer's body, if they still have one. Searched each frame rather than cached,
    /// because they can die and respawn while you are watching them and the new body is a
    /// different object.
    Transform Find()
    {
        if (target == null)
            return null;

        foreach (PlayerController player in
                 FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (player != null && player.View != null && player.View.Owner == target)
                return player.transform;
        }

        return null;
    }
}
