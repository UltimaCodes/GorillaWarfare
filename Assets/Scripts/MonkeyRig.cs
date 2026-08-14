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
    [Header("Model")]
    [SerializeField] string modelResource = "Models/Gorilla/gorilla";

    // The source is Z-up (Source engine SMD) and the fbx carries that through, so he imports
    // lying on his back. Stand him up here rather than re-exporting.
    [SerializeField] Vector3 modelRotation = new Vector3(-90f, 0f, 0f);

    // Bone names as data, not code - the last model used b_Spine02 style names, this one is
    // Rigify's DEF- convention. Swapping models shouldn't mean editing this file.
    [Header("Bones")]
    [SerializeField] string spineBone = "SPINE3";
    [SerializeField] string headBone = "Head";
    [SerializeField] string leftThighBone = "LEFTHIP";
    [SerializeField] string leftShinBone = "LEFTKNEE";
    [SerializeField] string rightThighBone = "RIGHTHIP";
    [SerializeField] string rightShinBone = "RIGHTKNEE";
    [SerializeField] string leftUpperArmBone = "LEFTSHOULDER";
    [SerializeField] string leftForeArmBone = "LEFTELBOW";
    [SerializeField] string rightUpperArmBone = "RIGHTSHOULDER";
    [SerializeField] string rightForeArmBone = "RIGHTELBOW";
    [SerializeField] string rightHandBone = "RIGHTHOLD";

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

    // Only input from outside. Everything else is measured here, because remote copies have
    // no PlayerMovement - it gets destroyed on them - but their transforms are replicated, so
    // position deltas tell us everything we need.
    public float LookPitch { get; set; }

    float planarSpeed;
    bool grounded = true;
    Vector3 lastPosition;

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
        model.transform.localRotation = Quaternion.Euler(modelRotation);

        // Belt and braces against the T-pose: if anything ever re-imports this as Humanoid or
        // Generic, the Animator would stamp its own pose over everything we write here.
        foreach (Animator stray in model.GetComponentsInChildren<Animator>(true))
            Destroy(stray);

        if (!CacheBones())
            return false;

        lastPosition = transform.position;

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
        spine = Find(spineBone);
        head = Find(headBone);

        leftThigh = Find(leftThighBone);
        leftShin = Find(leftShinBone);
        rightThigh = Find(rightThighBone);
        rightShin = Find(rightShinBone);

        leftUpperArm = Find(leftUpperArmBone);
        leftForeArm = Find(leftForeArmBone);
        rightUpperArm = Find(rightUpperArmBone);
        rightForeArm = Find(rightForeArmBone);

        RightHand = Find(rightHandBone);

        if (spine == null || head == null || leftThigh == null || rightThigh == null)
        {
            Debug.LogError($"Rig is missing bones. Looked for {spineBone}, {headBone}, {leftThighBone}, {rightThighBone}.", this);
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

        Measure();
        DriveLegs();
        DriveAim();
        DriveArms();
    }

    void Measure()
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        delta.y = 0f;
        lastPosition = position;

        float dt = Time.deltaTime;
        planarSpeed = dt > 0f ? delta.magnitude / dt : 0f;

        // Same probe FootstepPlayer uses: capsule is 2 tall on a centred pivot, so the feet sit
        // 1 below, plus slack for slopes and steps.
        grounded = Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, 1.45f,
                                   ~0, QueryTriggerInteraction.Ignore);
    }

    void DriveLegs()
    {
        float speed01 = Mathf.Clamp01(planarSpeed / referenceSpeed);

        // Phase advances with distance covered, not with time, so the legs stay in step with
        // the ground however fast you're going.
        gaitPhase += planarSpeed * strideRate * Time.deltaTime;

        // Airborne: hold a tucked pose instead of pedalling through the sky.
        float swing = grounded ? Mathf.Sin(gaitPhase) * maxLegSwing * speed01 : -18f;
        float otherSwing = grounded ? -swing : -18f;

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
