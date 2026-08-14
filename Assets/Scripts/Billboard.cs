using UnityEngine;

/// <summary>Turns a world-space nameplate to face the local player's camera.</summary>
public class Billboard : MonoBehaviour
{
    // LateUpdate, not Update: the camera moves in Update, so facing it from Update leaves the
    // nameplate one frame stale and visibly swimming when you turn quickly.
    void LateUpdate()
    {
        Camera cam = PlayerController.LocalCamera;
        if (cam == null)
            return;

        // Single assignment replaces LookAt + a 180 degree Rotate. Facing away from the camera
        // is what a billboard wants, since the text renders on the quad's forward face.
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
