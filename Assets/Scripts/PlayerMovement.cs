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

    /// <summary>
    /// Throws this player, for explosions and grapples.
    ///
    /// Added to the existing velocity rather than replacing it, so a blast taken while already
    /// moving sends you further - which is the entire skill of rocket jumping, and would be
    /// thrown away by assigning.
    ///
    /// Grounded is cleared as well. The air control rules read it, and a launch that leaves the
    /// player believing they are still standing on something gets damped away in a single frame.
    /// </summary>
    /// <summary>
    /// Throws the player, and makes the throw survive the next frame.
    ///
    /// Setting `grounded = false` here was not enough and this is why the pineapple felt like
    /// nothing. The very next Update does `grounded = controller.isGrounded`, which is still
    /// true because the character has not physically moved yet - so GroundMove ran, applied
    /// friction to the launch and clamped it back to walking pace. A fifteen metre a second
    /// blast became a shuffle before a single frame had been drawn.
    ///
    /// The window is what fixes it: for a tenth of a second after an impulse the mover is
    /// treated as airborne no matter what the controller says, which is long enough to
    /// physically leave the floor.
    /// </summary>
    /// <summary>
    /// Being hit bleeds momentum, unless you are nearly dead.
    ///
    /// A shot landing on somebody sprinting past should slow them down - it is the only way
    /// shooting at a moving target reads as having done anything before they die. Scaled to the
    /// damage, so a pellet is a stumble and a rocket is a stop.
    ///
    /// Deliberately does nothing while adrenaline is up. The whole point of the last of your
    /// health being the fastest part of the match is that it cannot be taken away by the person
    /// who put you there.
    /// </summary>
    public void Stagger(float fraction)
    {
        if (adrenaline > 0.01f)
            return;

        float keep = Mathf.Clamp01(1f - fraction);

        velocity.x *= keep;
        velocity.z *= keep;

        // The chain breaks. Taking a hit mid-chain and keeping the bonus would make the tech
        // free, and it should be a thing you can be knocked out of.
        chainExpires = -99f;
        chain = 0;
    }

    float adrenaline;

    /// <summary>
    /// How close to death the player is, as a speed multiplier from zero to one.
    ///
    /// Set by PlayerController, which owns health. Kept here because it is movement, and because
    /// having the mover ask about health every frame is the wrong way round.
    /// </summary>
    public void SetAdrenaline(float amount) => adrenaline = Mathf.Clamp01(amount);

    /// The multiplier currently applied to ground speed.
    public float AdrenalineSpeed => 1f + adrenaline * 0.28f;

    public void AddImpulse(Vector3 impulse)
    {
        velocity += impulse;
        grounded = false;
        launchedUntil = Time.time + launchWindow;

        // Past the jump buffer, so the ground move that runs next frame cannot decide you
        // wanted to jump and stomp the launch with its own vertical speed.
        jumpPressedAt = -99f;
    }

    [Tooltip("Seconds after a blast during which friction is ignored, so the launch survives "
             + "long enough to leave the ground.")]
    [SerializeField] float launchWindow = 0.12f;

    float launchedUntil = -99f;

    /// Whether a blast is still carrying the player. Public so anything that cares about speed
    /// - the field of view kick, the wind lines - can tell a launch from a sprint.
    public bool Launched => Time.time < launchedUntil;
    public bool Grounded => grounded;
    public float HorizontalSpeed => new Vector3(velocity.x, 0f, velocity.z).magnitude;

    [Header("Slide and crouch")]
    [Tooltip("Speed you must already be carrying for the slide key to slide rather than crouch. "
             + "Below it you simply go down.")]
    [SerializeField] float slideEntrySpeed = 6.5f;

    [Tooltip("Multiplier applied to your speed the moment a slide starts. Slightly over one, so "
             + "sliding into a fight is a commitment that pays rather than a way to stop.")]
    [SerializeField] float slideKick = 1.18f;

    [Tooltip("How fast a slide bleeds off, in metres per second per second. Lower slides "
             + "further.")]
    [SerializeField] float slideDrag = 7f;

    [Tooltip("Below this a slide has run out and becomes a crouch.")]
    [SerializeField] float slideExitSpeed = 3.5f;

    [Tooltip("How tall you are crouched, as a fraction of standing.")]
    [Range(0.35f, 0.9f)] [SerializeField] float crouchHeight = 0.55f;

    [Tooltip("How fast you can walk while crouched.")]
    [SerializeField] float crouchSpeed = 3.2f;

    [Tooltip("How quickly the camera drops and rises. Fast enough to feel responsive, slow "
             + "enough that a slide cancel reads as a movement rather than a teleport.")]
    [SerializeField] float stanceEase = 12f;

    [Header("Hopping")]
    [Tooltip("How long after landing a jump still counts, in seconds. Larger is more forgiving "
             + "and is what makes hopping in a straight line learnable.")]
    [SerializeField] float bhopGrace = 0.14f;

    [Tooltip("Fraction of your speed kept when you hop within the grace window instead of "
             + "taking a frame of friction. One means a perfect hop costs nothing.")]
    [Range(0.5f, 1f)] [SerializeField] float bhopKeep = 1f;

    CharacterController controller;
    Vector3 velocity;
    bool grounded;
    float jumpPressedAt = -1f;

    [Tooltip("How many slides in a row keep paying. After this the chain gives nothing until "
             + "you break it, so the technique has a ceiling rather than being infinite speed.")]
    [SerializeField] int maxChain = 4;

    [Tooltip("Extra kick per link in the chain, on top of the base slide boost.")]
    [SerializeField] float chainBonus = 0.09f;

    [Tooltip("Seconds you have after leaving a slide to start the next one and keep the chain.")]
    [SerializeField] float chainWindow = 0.9f;

    [Tooltip("Speed you get when you jump straight out of a slide, on top of what you had.")]
    [SerializeField] float slideJumpBoost = 1.6f;

    int chain;
    float chainExpires = -99f;

    /// How many slides deep the current chain is, for the camera and the effects. Zero when
    /// nothing is going on.
    public int SlideChain => Time.time < chainExpires ? chain : 0;

    bool sliding;
    bool crouching;
    float standingHeight;
    float standingCentre;
    float landedAt = -99f;

    /// Whether the player is currently sliding. Read by the camera, which leans into it, and by
    /// anything that wants to know why somebody is moving faster than they should be.
    public bool Sliding => sliding;
    public bool Crouching => crouching;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        standingHeight = controller.height;
        standingCentre = controller.center.y;
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

        // The settings screen is up, so the keyboard belongs to it. Without this you walk off
        // the map while rebinding your movement keys - every key you press to test a binding is
        // also still driving the player - and the same WASD that scrolls a list moves you.
        //
        // Gravity and collision carry on below regardless. Freezing in mid-air would be a
        // different kind of wrong.
        bool listening = SettingsMenu.IsOpen;

        if (KeyBinds.Pressed(KeyBinds.Action.Jump) && !listening)
            jumpPressedAt = Time.time;

        bool wantsJump = !listening
                         && (autoBhop
                             ? KeyBinds.Held(KeyBinds.Action.Jump)
                             : Time.time - jumpPressedAt <= jumpBufferTime);

        // The launch window overrides the controller. Without it a blast that has not yet moved
        // you off the floor is treated as a walk and its speed is thrown away by friction.
        bool wasAirborne = !grounded;
        grounded = controller.isGrounded && !Launched;

        // Landing is worth remembering, because a hop taken shortly after it should keep the
        // speed you arrived with rather than pay a frame of friction for touching the floor.
        if (grounded && wasAirborne)
            landedAt = Time.time;

        UpdateStance(listening, dt);

        Vector3 wishDir = WishDirection();

        float wishSpeed = (crouching ? crouchSpeed : maxGroundSpeed) * AdrenalineSpeed;

        if (grounded)
            GroundMove(wishDir, wishSpeed, wantsJump, dt);
        else
            AirMove(wishDir, wishSpeed, dt);

        controller.Move(velocity * dt);
    }

    /// <summary>
    /// Slide, crouch, or neither.
    ///
    /// One key does both and which one you get depends on whether you were going anywhere.
    /// Holding it at speed on the ground starts a slide; holding it standing still puts you in a
    /// crouch; a slide that runs out of speed becomes a crouch on its own. That is the whole
    /// rule, and it is the rule because it means you never have to decide which one you wanted -
    /// your momentum already decided.
    ///
    /// Slide cancelling falls out of it rather than being a special case: jumping out of a slide
    /// is just a jump, and a jump keeps your horizontal speed, so a slide into a hop carries the
    /// slide's boost into the air. Releasing the key mid-slide stands you straight up with the
    /// speed you had. Neither needed a line of its own.
    /// </summary>
    void UpdateStance(bool listening, float dt)
    {
        bool wants = !listening && KeyBinds.Held(KeyBinds.Action.Walk);
        float speed = HorizontalSpeed;

        if (!wants)
        {
            // Standing up is refused while there is something overhead, or you would be shoved
            // through the ceiling.
            if ((sliding || crouching) && !Blocked())
            {
                sliding = false;
                crouching = false;
            }
        }
        else if (!sliding && !crouching)
        {
            // Which one you get is decided once, on the press, by how fast you were already
            // going. Deciding it every frame would flicker between the two at the boundary.
            if (grounded && speed >= slideEntrySpeed)
            {
                sliding = true;

                // Chaining. Each slide taken shortly after the last one pays a little more, up
                // to a ceiling - so slide, hop, slide, hop builds speed and is worth learning,
                // but it tops out rather than turning into infinite acceleration. Break the
                // rhythm and you start again from the base kick.
                chain = Time.time < chainExpires ? Mathf.Min(chain + 1, maxChain) : 1;
                chainExpires = Time.time + chainWindow;

                float kick = slideKick + chainBonus * (chain - 1);

                // The kick is what makes sliding a decision rather than a brake. Applied to the
                // direction you are already travelling, not the one you are looking - a slide
                // goes where you were going.
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
                flat *= kick;

                velocity.x = flat.x;
                velocity.z = flat.z;

                // Rising in pitch with the chain, so the fourth one sounds like the fourth one.
                GameAudio.PlayAtDelayed(GameAudio.Slide, transform.position,
                                        GameAudio.SlideVolume, 0.9f + 0.07f * chain, 0f);
            }
            else
            {
                crouching = true;
            }
        }
        else if (sliding && (speed < slideExitSpeed || !grounded))
        {
            // Run out of speed and you end up crouched, which is where a slide naturally ends.
            // Leave the ground and it ends too - sliding through the air is not a thing.
            sliding = false;
            crouching = grounded;
        }

        // The capsule follows the stance, eased rather than snapped, so the camera drops into a
        // slide instead of teleporting down.
        float wantedHeight = sliding || crouching ? standingHeight * crouchHeight : standingHeight;

        controller.height = Mathf.MoveTowards(controller.height, wantedHeight, stanceEase * dt);

        Vector3 centre = controller.center;
        centre.y = standingCentre - (standingHeight - controller.height) * 0.5f;
        controller.center = centre;
    }

    /// Whether there is something directly overhead, so standing up would clip through it.
    bool Blocked()
    {
        Vector3 top = transform.position + Vector3.up * (controller.height * 0.5f);
        float need = standingHeight - controller.height + 0.1f;

        return need > 0.01f
               && Physics.SphereCast(top, controller.radius * 0.9f, Vector3.up, out _, need,
                                     Hitbox.WorldMask, QueryTriggerInteraction.Ignore);
    }

    Vector3 WishDirection()
    {
        // From the four bound keys rather than the Input Manager's axes, which can only be
        // changed in a project settings window - not by the person playing.
        Vector2 move = SettingsMenu.IsOpen ? Vector2.zero : KeyBinds.MoveAxis();
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

            // Jumping out of a slide is the cancel, and it is also the link between one slide
            // and the next. The horizontal speed is already yours and a jump does not touch it,
            // so the boost carries into the air on its own - the extra shove here is what makes
            // the hop worth taking rather than merely free.
            if (sliding)
            {
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

                if (flat.sqrMagnitude > 0.01f)
                {
                    flat = flat.normalized * slideJumpBoost;
                    velocity.x += flat.x;
                    velocity.z += flat.z;
                }

                // The window is refreshed rather than started, so the chain survives the time
                // spent in the air between two slides.
                chainExpires = Time.time + chainWindow;
            }

            sliding = false;

            Accelerate(wishDir, wishSpeed, groundAccel, dt);
            return;
        }

        // A slide bleeds off on its own schedule rather than taking ground friction, which is
        // what makes it travel: friction is tuned to stop you in about a step and a half.
        if (sliding)
        {
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            float speed = flat.magnitude;

            if (speed > 0.01f)
            {
                float slowed = Mathf.Max(0f, speed - slideDrag * dt);
                flat = flat / speed * slowed;

                velocity.x = flat.x;
                velocity.z = flat.z;
            }

            // No steering input while sliding. You committed to a direction; the mouse still
            // turns your view and your aim, but the slide goes where it was going.
            velocity.y = -2f;
            return;
        }

        // A hop taken shortly after landing skips friction entirely, which is what makes
        // bunnyhopping in a straight line learnable rather than frame perfect. Without it every
        // touch of the floor costs speed and only an exact landing-frame jump keeps any.
        if (Time.time - landedAt > bhopGrace)
            ApplyFriction(dt);
        else if (bhopKeep < 1f)
            velocity *= bhopKeep;

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
