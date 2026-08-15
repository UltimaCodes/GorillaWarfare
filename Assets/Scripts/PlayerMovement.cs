using UnityEngine;

// Quake/Source style movement.
//
// The old version did Vector3.SmoothDamp toward a target velocity and pushed the rigidbody with
// MovePosition, which bypasses the solver entirely. That has no momentum, so bunnyhopping and
// air strafing weren't just missing, they were impossible.
//
// Three bits of maths make this feel right:
//
//  1. Friction is applied to the whole velocity vector each frame, not per axis, and stops
//     applying the moment you leave the ground. That's why you keep speed in the air.
//
//  2. Accelerate() only adds speed along the direction you're asking for, and only up to
//     wishSpeed *measured along that direction*. Once you're already going that fast that way,
//     it does nothing.
//
//  3. In the air, wishSpeed is clamped to airSpeedCap - a very small number. So pointing where
//     you're already going gains you nothing, but pointing slightly off means the dot product is
//     small and there's headroom to add speed. Turn the mouse while holding a strafe key and you
//     accelerate. That's the whole trick, and it falls out of the maths rather than being
//     special-cased.
//
// Added at runtime by PlayerController so there's nothing to wire on the prefab.
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    // Numbers converted from Quake 3 / Source defaults rather than made up. Both engines work
    // in units of 1 inch, so units/s * 0.0254 = m/s.
    //
    //   g_speed 320        -> 8.13 m/s
    //   pm_accelerate 10   -> 10 (unitless)
    //   pm_friction 6      -> 6  (unitless)
    //   pm_stopspeed 100   -> 2.54 m/s
    //   g_gravity 800      -> 20.32 m/s^2
    //   jump velocity 270  -> 6.86 m/s
    //   sv_airaccelerate   -> 100 (CS:S value; Q3 uses 1, which is far stiffer)
    //   sv_maxairspeed 30  -> 0.762 m/s

    [Header("Ground")]
    [SerializeField] float maxGroundSpeed = 8.13f;
    [SerializeField] float groundAccel = 10f;
    [SerializeField] float friction = 6f;
    [SerializeField] float stopSpeed = 2.54f;

    [Header("Walk")]
    [SerializeField] float walkSpeed = 3.4f;

    [Header("Air")]
    [SerializeField] float airAccel = 100f;
    [SerializeField] float airSpeedCap = 0.762f;

    [Header("Jump")]
    [SerializeField] float jumpSpeed = 6.86f;
    [SerializeField] float gravity = 20.32f;

    // Off: you have to time each jump. On, holding space keeps your speed for free, which is
    // most of the skill gone.
    [SerializeField] bool autoBhop = false;

    // Keeps a jump you pressed slightly too early, so landing and immediately jumping again
    // is forgiving instead of frame-perfect.
    [SerializeField] float jumpBufferTime = 0.1f;

    public Vector3 Velocity => velocity;
    public bool Grounded => grounded;
    public float HorizontalSpeed => new Vector3(velocity.x, 0f, velocity.z).magnitude;

    CharacterController controller;
    Vector3 velocity;
    bool grounded;
    float jumpPressedAt = -1f;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f)
            return;

        // Once the match is over you stand still and read the scoreboard. Gravity still runs,
        // so anyone who died mid-air lands rather than hanging there.
        if (MatchState.Phase == MatchPhase.Over)
        {
            velocity.x = 0f;
            velocity.z = 0f;
            velocity.y -= gravity * dt;
            controller.Move(velocity * dt);
            return;
        }

        if (KeyBinds.Pressed(KeyBinds.Action.Jump))
            jumpPressedAt = Time.time;

        bool wantsJump = autoBhop
            ? KeyBinds.Held(KeyBinds.Action.Jump)
            : Time.time - jumpPressedAt <= jumpBufferTime;

        grounded = controller.isGrounded;

        Vector3 wishDir = WishDirection();
        float wishSpeed = KeyBinds.Held(KeyBinds.Action.Walk) ? walkSpeed : maxGroundSpeed;

        if (grounded)
            GroundMove(wishDir, wishSpeed, wantsJump, dt);
        else
            AirMove(wishDir, wishSpeed, dt);

        controller.Move(velocity * dt);
    }

    Vector3 WishDirection()
    {
        // From the four bound keys rather than the Input Manager's axes, which can only be
        // changed in a project settings window - not by the person playing.
        Vector2 move = KeyBinds.MoveAxis();
        float x = move.x;
        float z = move.y;

        // Not normalised across both axes beyond magnitude 1 - diagonal shouldn't be faster,
        // but the direction itself is what matters to Accelerate.
        Vector3 dir = transform.right * x + transform.forward * z;
        dir.y = 0f;

        return dir.sqrMagnitude > 1f ? dir.normalized : dir;
    }

    void GroundMove(Vector3 wishDir, float wishSpeed, bool wantsJump, float dt)
    {
        // Jumping is handled before friction, otherwise the frame you land and take off again
        // still eats a full frame of friction and bunnyhopping bleeds speed.
        if (wantsJump)
        {
            jumpPressedAt = -1f;
            velocity.y = jumpSpeed;
            grounded = false;
            Accelerate(wishDir, wishSpeed, groundAccel, dt);
            return;
        }

        ApplyFriction(dt);
        Accelerate(wishDir, wishSpeed, groundAccel, dt);

        // Small constant push into the floor. CharacterController.isGrounded only reports what
        // the last Move() found, so without this it flickers on slopes and steps.
        velocity.y = -2f;
    }

    void AirMove(Vector3 wishDir, float wishSpeed, float dt)
    {
        // No friction in the air - this is what preserves momentum between hops.
        Accelerate(wishDir, Mathf.Min(wishSpeed, airSpeedCap), airAccel, dt);
        velocity.y -= gravity * dt;
    }

    void ApplyFriction(float dt)
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;

        if (speed < 0.01f)
        {
            velocity.x = 0f;
            velocity.z = 0f;
            return;
        }

        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * dt;
        float scale = Mathf.Max(speed - drop, 0f) / speed;

        velocity.x *= scale;
        velocity.z *= scale;
    }

    // The heart of it. Speed is measured *along wishDir*, so once you're already moving that
    // fast in that direction there's nothing left to add - but a direction you're not already
    // travelling in always has room.
    void Accelerate(Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float currentSpeed = Vector3.Dot(velocity, wishDir);
        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0f)
            return;

        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, addSpeed);
        velocity += wishDir * accelSpeed;
    }

    // Called on respawn so you don't keep last life's momentum.
    public void ResetVelocity()
    {
        velocity = Vector3.zero;
    }
}
