using UnityEngine;

// Procedural animation. No clips, no AnimatorController - the bones get driven directly.
//
// We only need three things: legs that move when you move, a head that looks where the camera
// looks, and hands on the weapon. A clip library would be far more work for far more than we
// want, and it'd need retargeting and blend trees on top.
//
// Everything here is driven by values that are already replicated - world position (so speed
// falls out of it) and the pitch float PlayerController sends. So remote monkeys animate
// correctly without a single extra byte on the wire.
public class MonkeyRig : MonoBehaviour
{
    const string modelResource = "Models/Monkey/monkey";

    [Header("Gait")]
    [SerializeField] float strideRate = 2.6f;      // swings per metre travelled
    [SerializeField] float maxLegSwing = 38f;      // degrees at full speed
    [SerializeField] float referenceSpeed = 7f;    // speed at which the swing maxes out
    [SerializeField] float kneeBend = 24f;

    [Header("Aim")]
    [SerializeField] float spineShare = 0.45f;     // how much of the pitch the chest takes
    [SerializeField] float headShare = 0.55f;      // ...and the head. Should sum to ~1.

    [Header("Arms")]
    [SerializeField] float armDown = 55f;          // shoulder rotation into a holding pose
    [SerializeField] float elbowBend = 65f;

    public float LookPitch { get; set; }
    public float PlanarSpeed { get; set; }
    public bool Grounded { get; set; } = true;

    GameObject model;
    Transform spine, head;
    Transform leftThigh, leftShin, rightThigh, rightShin;
    Transform leftUpperArm, leftForeArm, rightUpperArm, rightForeArm;

    Quaternion spineRest, headRest;
    Quaternion leftThighRest, leftShinRest, rightThighRest, rightShinRest;
    Quaternion leftUpperRest, leftForeRest, rightUpperRest, rightForeRest;

    float gaitPhase;

    public Transform RightHand { get; private set; }

    public bool Build(bool hideFromOwner)
    {
        GameObject prefab = Resources.Load<GameObject>(modelResource);
        if (prefab == null)
        {
            Debug.LogError($"No model at Resources/{modelResource}", this);
            return false;
        }

        model = Instantiate(prefab, transform);

        // Capsule pivot is at the middle, model pivot is at the feet.
        model.transform.localPosition = new Vector3(0f, -1f, 0f);
        model.transform.localRotation = Quaternion.identity;

        if (!CacheBones())
            return false;

        if (hideFromOwner)
        {
            // First person - you shouldn't see your own body from inside its head, but the
            // shadow should still be there.
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        return true;
    }

    bool CacheBones()
    {
        spine = Find("b_Spine02");
        head = Find("b_Head");

        leftThigh = Find("b_Left_Leg01");
        leftShin = Find("b_Left_Leg02");
        rightThigh = Find("b_Right_Leg01");
        rightShin = Find("b_Right_Leg02");

        leftUpperArm = Find("b_Left_UpperArm");
        leftForeArm = Find("b_Left_ForeArm");
        rightUpperArm = Find("b_Right_UpperArm");
        rightForeArm = Find("b_Right_ForeArm");

        RightHand = Find("b_Right_Hand");

        if (spine == null || head == null || leftThigh == null || rightThigh == null)
        {
            Debug.LogError("Monkey rig is missing expected bones - did the model change?", this);
            return false;
        }

        spineRest = spine.localRotation;
        headRest = head.localRotation;
        leftThighRest = leftThigh.localRotation;
        rightThighRest = rightThigh.localRotation;
        leftUpperRest = leftUpperArm != null ? leftUpperArm.localRotation : Quaternion.identity;
        rightUpperRest = rightUpperArm != null ? rightUpperArm.localRotation : Quaternion.identity;

        if (leftShin != null) leftShinRest = leftShin.localRotation;
        if (rightShin != null) rightShinRest = rightShin.localRotation;
        if (leftForeArm != null) leftForeRest = leftForeArm.localRotation;
        if (rightForeArm != null) rightForeRest = rightForeArm.localRotation;

        return true;
    }

    Transform Find(string boneName)
    {
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == boneName)
                return t;
        }

        return null;
    }

    // LateUpdate so we're writing bones after anything else has had its say this frame.
    void LateUpdate()
    {
        if (model == null)
            return;

        DriveLegs();
        DriveAim();
        DriveArms();
    }

    void DriveLegs()
    {
        float speed01 = Mathf.Clamp01(PlanarSpeed / referenceSpeed);

        // Phase advances with distance covered, not with time, so the legs stay in step with
        // the ground however fast you're going.
        gaitPhase += PlanarSpeed * strideRate * Time.deltaTime;

        // Airborne: hold a tucked pose instead of pedalling through the sky.
        float swing = Grounded ? Mathf.Sin(gaitPhase) * maxLegSwing * speed01 : -18f;
        float otherSwing = Grounded ? -swing : -18f;

        leftThigh.localRotation = leftThighRest * Quaternion.Euler(swing, 0f, 0f);
        rightThigh.localRotation = rightThighRest * Quaternion.Euler(otherSwing, 0f, 0f);

        // Knee only bends as the leg comes forward, which is what stops it looking like a
        // marionette.
        if (leftShin != null)
            leftShin.localRotation = leftShinRest * Quaternion.Euler(Mathf.Max(0f, -swing) * (kneeBend / maxLegSwing), 0f, 0f);

        if (rightShin != null)
            rightShin.localRotation = rightShinRest * Quaternion.Euler(Mathf.Max(0f, -otherSwing) * (kneeBend / maxLegSwing), 0f, 0f);
    }

    void DriveAim()
    {
        // Split between chest and head so it doesn't look like an owl.
        spine.localRotation = spineRest * Quaternion.Euler(LookPitch * spineShare, 0f, 0f);
        head.localRotation = headRest * Quaternion.Euler(LookPitch * headShare, 0f, 0f);
    }

    void DriveArms()
    {
        // Static hold rather than IK. The weapon is parented to the hand, so the hands are on
        // it by construction - they just need to be up and forward rather than hanging.
        if (leftUpperArm != null)
            leftUpperArm.localRotation = leftUpperRest * Quaternion.Euler(-armDown, 0f, 0f);

        if (rightUpperArm != null)
            rightUpperArm.localRotation = rightUpperRest * Quaternion.Euler(-armDown, 0f, 0f);

        if (leftForeArm != null)
            leftForeArm.localRotation = leftForeRest * Quaternion.Euler(-elbowBend, 0f, 0f);

        if (rightForeArm != null)
            rightForeArm.localRotation = rightForeRest * Quaternion.Euler(-elbowBend, 0f, 0f);
    }
}
