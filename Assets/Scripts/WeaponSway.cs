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

    void Start()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        lastOwnerPosition = transform.position;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // --- sway from looking around
        float mx = Input.GetAxisRaw("Mouse X");
        float my = Input.GetAxisRaw("Mouse Y");

        Vector3 swayOffset = new Vector3(
            Mathf.Clamp(-mx * swayAmount, -maxSway, maxSway),
            Mathf.Clamp(-my * swayAmount, -maxSway, maxSway),
            0f);

        Quaternion swayTilt = Quaternion.Euler(my * swayRotation, -mx * swayRotation, -mx * swayRotation);

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
            0f);

        float t = 1f - Mathf.Exp(-swaySmooth * dt);
        transform.localPosition = Vector3.Lerp(transform.localPosition, restPosition + swayOffset + bob, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, restRotation * swayTilt, t);
    }
}
