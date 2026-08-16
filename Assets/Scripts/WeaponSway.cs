using UnityEngine;

// Makes the held weapon lag behind the camera and bob while walking.
//
// This is doing a surprising amount of work for how simple it is: a weapon rigidly welded to the
// camera reads as a UI element rather than an object, and the lag is what sells it as something
// with mass that the character is carrying. Bob does the same job for movement - without it,
// walking feels like sliding.
//
// Everything is local offsets on the item holder, so it never touches where you're actually
// aiming. The raycast still comes from the camera centre.
public class WeaponSway : MonoBehaviour
{
    [Header("Sway")]
    [SerializeField] float swayAmount = 0.02f;
    [SerializeField] float swayRotation = 4f;
    [SerializeField] float swaySmooth = 8f;
    [SerializeField] float maxSway = 0.06f;

    [Header("Bob")]
    [SerializeField] float bobDistance = 0.018f;
    [SerializeField] float bobRate = 9f;

    // Speed at which bob reaches full strength. Matches the movement's reference speed so a
    // sprint bobs fully and a walk only partly.
    [SerializeField] float referenceSpeed = 8f;

    Vector3 restPosition;
    Quaternion restRotation;

    Vector3 lastOwnerPosition;
    float bobPhase;

    // Damped right down while aiming. A weapon that wanders is fine from the hip and useless
    // through a scope, where the whole point is that the barrel sits still.
    [SerializeField] float aimDamping = 0.15f;

    float damping = 1f;

    void Start()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        lastOwnerPosition = transform.position;
    }

    /// <summary>
    /// Where the weapon should settle back to. Aiming moves the holder, so the rest pose is
    /// handed in each frame rather than captured once - otherwise sway keeps dragging the
    /// weapon back to where it sat before you raised it, and the two fight over the transform.
    /// </summary>
    public void SetRest(Vector3 position, Quaternion rotation, bool aiming)
    {
        restPosition = position;
        restRotation = rotation;
        damping = aiming ? aimDamping : 1f;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // --- sway from looking around
        // The camera already refuses to turn while settings are open; the weapon swaying to a
        // mouse that is dragging a slider is the same bug one layer down, and it looks like the
        // gun is trying to follow the cursor.
        bool free = !SettingsMenu.IsOpen;

        float mx = free ? Input.GetAxisRaw("Mouse X") : 0f;
        float my = free ? Input.GetAxisRaw("Mouse Y") : 0f;

        Vector3 swayOffset = new Vector3(
            Mathf.Clamp(-mx * swayAmount, -maxSway, maxSway),
            Mathf.Clamp(-my * swayAmount, -maxSway, maxSway),
            0f) * damping;

        Quaternion swayTilt = Quaternion.Euler(my * swayRotation * damping,
                                               -mx * swayRotation * damping,
                                               -mx * swayRotation * damping);

        // --- bob from moving
        Vector3 delta = transform.position - lastOwnerPosition;
        delta.y = 0f;
        lastOwnerPosition = transform.position;

        float speed = delta.magnitude / dt;
        float strength = Mathf.Clamp01(speed / referenceSpeed);

        // Phase advances with distance, not time, so the bob stays in step with the stride
        // instead of running at a fixed rate regardless of how fast you're going.
        bobPhase += delta.magnitude * bobRate;

        // Figure of eight: horizontal at half the vertical rate, which is what makes it read as
        // footfalls rather than a bounce.
        Vector3 bob = new Vector3(
            Mathf.Sin(bobPhase * 0.5f) * bobDistance * strength,
            -Mathf.Abs(Mathf.Sin(bobPhase)) * bobDistance * strength,
            0f) * damping;

        float t = 1f - Mathf.Exp(-swaySmooth * dt);
        transform.localPosition = Vector3.Lerp(transform.localPosition, restPosition + swayOffset + bob, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, restRotation * swayTilt, t);
    }
}
