using System.Collections.Generic;
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
    // Where each hand should be, measured from the chest, in metres. Forward, right, up.
    //
    // The right hand is the one the weapon hangs off, so this is effectively where the banana
    // sits. The left is further along it and closer to the middle, which is what reads as a
    // second hand supporting the thing rather than a second arm doing its own reaching.
    [SerializeField] Vector3 rightGrip = new Vector3(0.34f, 0.17f, -0.02f);
    [SerializeField] Vector3 leftGrip = new Vector3(0.46f, 0.01f, 0.03f);

    // A pistol is one banana in one fist, held further in and a little higher - you don't
    // brace a revolver with your off hand the way you brace a rifle.
    [SerializeField] Vector3 pistolGrip = new Vector3(0.40f, 0.11f, 0.04f);

    // Where the off hand goes when it has nothing to hold: down by the hip, slightly out. This
    // rig's rest pose is arms straight out to the sides, so leaving the arm alone is not an
    // option - "do nothing" looks exactly like the zombie reach this was meant to fix.
    [SerializeField] Vector3 idleHand = new Vector3(0.04f, -0.26f, -0.44f);

    // Which way the elbows break. Down and outward, like someone holding something, rather than
    // out to the sides like someone being arrested.
    [SerializeField] float elbowDrop = 0.8f;
    [SerializeField] float elbowFlare = 0.6f;

    /// Set by PlayerController from whatever is equipped. False puts the off hand away.
    public bool TwoHandedGrip { get; set; } = true;

    // Only input from outside. Everything else is measured here, because remote copies have
    // no PlayerMovement - it gets destroyed on them - but their transforms are replicated, so
    // position deltas tell us everything we need.
    public float LookPitch { get; set; }

    /// Exposed so the editor check can report why the gait isn't moving.
    public bool GroundedForTest => grounded;
    public string DebugState => $"speed={planarSpeed:F2} dist={distanceThisFrame:F3} phase={gaitPhase:F2} grounded={grounded}";

    float planarSpeed;
    bool grounded = true;
    Vector3 lastPosition;

    GameObject model;
    Transform spine, head;
    Transform leftThigh, leftShin, rightThigh, rightShin;
    Transform leftUpperArm, leftForeArm, rightUpperArm, rightForeArm;

    // Set once from the rest pose: the direction each bone points in its own local space, and
    // how long it is. Derived rather than assumed, because which local axis runs down a bone is
    // a decision the person who rigged the model made, not a convention.
    Vector3 leftUpperAim, leftForeAim, rightUpperAim, rightForeAim;
    float leftUpperLength, leftForeLength, rightUpperLength, rightForeLength;
    Transform leftHand, rightHandEnd;
    bool armsSolvable;

    Quaternion spineRest, headRest;
    Quaternion leftThighRest, leftShinRest, rightThighRest, rightShinRest;
    Quaternion leftUpperRest, leftForeRest, rightUpperRest, rightForeRest;

    float gaitPhase;
    float distanceThisFrame;

    public Transform RightHand { get; private set; }

    /// <summary>
    /// Paints the body.
    ///
    /// Through a MaterialPropertyBlock rather than by touching the material, because assigning
    /// to `renderer.material` instantiates a copy per player - five gorillas would mean five
    /// materials, five draw call batches broken, and five leaks when they respawn. A property
    /// block overrides the colour for one renderer and shares everything else.
    ///
    /// Only the body. The weapons have their own colours and a banana that turned team red
    /// would stop reading as a banana.
    /// </summary>
    public void Tint(Color colour)
    {
        if (model == null)
            return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();

        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(block);

            // Both names: the built-in pipeline's standard shader calls it _Color and most
            // everything since calls it _BaseColor. Setting a property a shader does not have
            // is free, so this does not need to know which one it got.
            block.SetColor("_Color", colour);
            block.SetColor("_BaseColor", colour);

            r.SetPropertyBlock(block);
        }
    }

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
        // One traversal for all eleven bones. It used to walk the entire skeleton once per
        // bone, and a player respawning is a player rebuilding its rig.
        bones.Clear();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            bones[t.name] = t;

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

        MeasureArms();

        return true;
    }

    // The hand at the end of each arm, and how long the two segments are. Taken from the model's
    // rest pose, which is the only moment it's guaranteed to be unposed.
    void MeasureArms()
    {
        leftHand = RightHand != null && RightHand.parent == leftForeArm ? RightHand : FirstChild(leftForeArm);
        rightHandEnd = RightHand != null && RightHand.parent == rightForeArm ? RightHand : FirstChild(rightForeArm);

        armsSolvable =
            leftUpperArm != null && leftForeArm != null && leftHand != null &&
            rightUpperArm != null && rightForeArm != null && rightHandEnd != null;

        if (!armsSolvable)
        {
            Debug.LogWarning("[rig] arms are missing a joint - falling back to a static hold.", this);
            return;
        }

        leftUpperAim = LocalAim(leftUpperArm, leftForeArm);
        leftForeAim = LocalAim(leftForeArm, leftHand);
        rightUpperAim = LocalAim(rightUpperArm, rightForeArm);
        rightForeAim = LocalAim(rightForeArm, rightHandEnd);

        leftUpperLength = Vector3.Distance(leftUpperArm.position, leftForeArm.position);
        leftForeLength = Vector3.Distance(leftForeArm.position, leftHand.position);
        rightUpperLength = Vector3.Distance(rightUpperArm.position, rightForeArm.position);
        rightForeLength = Vector3.Distance(rightForeArm.position, rightHandEnd.position);
    }

    static Transform FirstChild(Transform bone)
    {
        return bone != null && bone.childCount > 0 ? bone.GetChild(0) : null;
    }

    /// The direction a bone points, expressed in its own local space.
    static Vector3 LocalAim(Transform bone, Transform child)
    {
        Vector3 world = (child.position - bone.position).normalized;
        return Quaternion.Inverse(bone.rotation) * world;
    }

    readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();

    Transform Find(string boneName)
    {
        return bones.TryGetValue(boneName, out Transform bone) ? bone : null;
    }

    // LateUpdate so we're writing bones after anything else has had its say this frame.
    void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    // Time comes in as a parameter so this can be driven outside play mode, where deltaTime is
    // always 0 and the gait could never be exercised. Nothing else about it changes.
    public void Tick(float dt)
    {
        if (model == null)
            return;

        Measure(dt);
        DriveLegs();
        DriveAim();
        DriveArms();
    }

    void Measure(float dt)
    {
        Vector3 position = transform.position;
        Vector3 delta = position - lastPosition;
        delta.y = 0f;
        lastPosition = position;

        distanceThisFrame = delta.magnitude;

        // Hold the last speed through a zero-length frame rather than dropping to standstill;
        // a hitch shouldn't snap the legs straight.
        if (dt > 0f)
            planarSpeed = distanceThisFrame / dt;

        // Same probe FootstepPlayer uses: capsule is 2 tall on a centred pivot, so the feet sit
        // 1 below, plus slack for slopes and steps.
        grounded = Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, 1.45f,
                                   Hitbox.WorldMask, QueryTriggerInteraction.Ignore);
    }

    void DriveLegs()
    {
        float speed01 = Mathf.Clamp01(planarSpeed / referenceSpeed);

        // Advance by distance actually travelled. It used to compute speed by dividing by
        // deltaTime and then multiply deltaTime straight back in, which is the same number with
        // a division by zero waiting in it - the legs froze solid any frame dt was 0.
        gaitPhase += distanceThisFrame * strideRate;

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

    // Two bone IK, one arm at a time, aimed at a point in front of the chest.
    //
    // The old version rotated each bone by a fixed angle, which cannot hold anything: the hand
    // ends up wherever the angles happen to put it, and on this rig that was straight out in
    // front like a zombie. Solving for a target instead means the hands go where the weapon is
    // and the elbows bend to suit, which is the difference between reaching and gripping.
    //
    // Runs after DriveAim, so the chest has already pitched and the targets come with it - aim
    // up and the hands follow, without anything extra being replicated.
    void DriveArms()
    {
        if (!armsSolvable || spine == null)
            return;

        SolveArm(rightUpperArm, rightForeArm, rightUpperAim, rightForeAim,
                 rightUpperLength, rightForeLength,
                 ChestPoint(TwoHandedGrip ? rightGrip : pistolGrip), 1f);

        if (TwoHandedGrip)
        {
            SolveArm(leftUpperArm, leftForeArm, leftUpperAim, leftForeAim,
                     leftUpperLength, leftForeLength, ChestPoint(leftGrip), -1f);
        }
        else
        {
            // Solved to the hip rather than left at rest. Rest is a T-pose on this model, so
            // doing nothing puts the arm straight out sideways.
            SolveArm(leftUpperArm, leftForeArm, leftUpperAim, leftForeAim,
                     leftUpperLength, leftForeLength, ChestPoint(idleHand), -1f);
        }
    }

    Vector3 ChestPoint(Vector3 offset)
    {
        return spine.position
               + transform.forward * offset.x
               + transform.right * offset.y
               + transform.up * offset.z;
    }

    void SolveArm(Transform upper, Transform fore, Vector3 upperAim, Vector3 foreAim,
                  float upperLength, float foreLength, Vector3 target, float side)
    {
        Vector3 shoulder = upper.position;
        Vector3 toTarget = target - shoulder;

        float reach = toTarget.magnitude;
        if (reach < 0.0001f)
            return;

        Vector3 direction = toTarget / reach;

        // Clamped inside the arm's actual reach, or the law of cosines below goes imaginary and
        // the arm snaps somewhere absurd.
        float span = Mathf.Clamp(reach,
                                 Mathf.Abs(upperLength - foreLength) + 0.01f,
                                 upperLength + foreLength - 0.01f);

        // Angle at the shoulder between the line to the target and the upper arm itself.
        float cosine = (upperLength * upperLength + span * span - foreLength * foreLength)
                       / (2f * upperLength * span);
        float bend = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;

        // The plane the elbow swings in. Down and out, so it reads as holding rather than
        // presenting.
        Vector3 pole = (-transform.up * elbowDrop + transform.right * side * elbowFlare).normalized;

        Vector3 axis = Vector3.Cross(direction, pole);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(direction, transform.forward);

        axis.Normalize();

        Vector3 upperDirection = Quaternion.AngleAxis(bend, axis) * direction;
        AimBone(upper, upperAim, upperDirection);

        // The forearm has moved with its parent, so where the elbow now is has to be read back
        // rather than predicted.
        Vector3 elbow = fore.position;
        Vector3 foreDirection = target - elbow;

        if (foreDirection.sqrMagnitude > 0.0001f)
            AimBone(fore, foreAim, foreDirection.normalized);
    }

    /// Turns a bone so the axis that runs down it points along a world direction.
    static void AimBone(Transform bone, Vector3 localAim, Vector3 worldDirection)
    {
        Vector3 current = bone.rotation * localAim;
        bone.rotation = Quaternion.FromToRotation(current, worldDirection) * bone.rotation;
    }
}
