using UnityEngine;

// Keeps a nameplate facing the local camera.
public class Billboard : MonoBehaviour
{
    // LateUpdate so we're not a frame behind the camera when you spin quickly.
    void LateUpdate()
    {
        Camera cam = PlayerController.LocalCamera;
        if (cam == null)
            return;

        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
