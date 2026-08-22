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
    [SerializeField] float airAccel = 130f;

    [Tooltip("Raised from the authentic CS:S value (0.762) on 2026-08-22. That number is tiny by "
             + "design - it's what makes real air-strafe technique (turning the mouse while "
             + "holding a strafe key) the only way to gain speed in the air, which is deep but "
             + "has a real learning curve. Reported back as 'too hard to change direction' "
             + "specifically about slide-hop chains, where the point is chaining hops together "
             + "rather than mastering strafe-jumping on its own. Raised enough that ordinary "
             + "directional input now visibly redirects a hop's trajectory - still well under "
             + "ground speed, so it curves a hop rather than cancels its momentum, and a player "
             + "who does strafe properly still gains more than one who doesn't.")]
    [SerializeField] float airSpeedCap = 2.5f;

    [Header("Jump")]
    [SerializeField] float jumpSpeed = 6.86f;
    [SerializeField] float gravity = 20.32f;

    // Off: you have to time each jump. On, holding space keeps your speed for free, which is
    // most of the skill gone.
    [SerializeField] bool autoBhop = false;

    // Keeps a jump you pressed slightly too early, so landing and immediately jumping again
    // is forgiving instead of frame-perfect. Matched to slideBuffer's window rather than tuned
    // separately - queuing a jump for landing and queuing a slide for landing are the same idea
    // played on two different keys, and a jump that was stingier about it than a slide made the
    // pair feel like two different systems instead of one.
    [SerializeField] float jumpBufferTime = 0.22f;

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

    [Tooltip("Hard ceiling on horizontal speed, in metres per second. New 2026-08-21: nothing "
             + "previously capped total velocity, only how often a slide chain or an air-strafed "
             + "bhop could add more to it - wishSpeed and airSpeedCap only ever bounded the "
             + "*target* Accelerate() chases, never the momentum already carried. Set well above "
             + "anything a legitimate rocket jump should reach (Pineapple fireKnockback is 26, "
             + "selfKnockback 14, and adrenaline adds another 28% near death) so it is a safety "
             + "ceiling on runaway stacking, not a nerf on the intended tech.")]
    [SerializeField] float maxHorizontalSpeed = 45f;

    /// <summary>
    /// Speed-scaled damage, shared by the peel's momentum melee and the vine's contact hit.
    ///
    /// One formula rather than two so there is only one curve to tune, and so the vine and the
    /// peel read as the same underlying idea - go fast, hit something, the speed is the damage -
    /// in two different shells. Below runSpeed the multiplier bottoms out at 0.6 rather than 0,
    /// so a melee swing standing still is weak, not useless; above chainSpeed (roughly a
    /// well-chained slide) it tops out at 2.0, which puts a full-speed hit within reach of a
    /// one-hit kill against a full health bar without making every graze at running pace lethal.
    /// </summary>
    public static float MomentumDamage(float baseDamage, float speed)
    {
        const float lowSpeed = 4f;
        const float highSpeed = 24f;
        const float lowMultiplier = 0.6f;
        const float highMultiplier = 2.0f;

        float t = Mathf.InverseLerp(lowSpeed, highSpeed, speed);
        return baseDamage * Mathf.Lerp(lowMultiplier, highMultiplier, t);
    }

    /// <summary>
    /// Set by VineGrapple while attached. Movement hands control over entirely rather than
    /// blending with it, the same way a launch overrides ground friction - a pull that still had
    /// to fight WASD and slide/crouch state would never read as being reeled in.
    /// </summary>
    public bool Grappling { get; set; }

    /// <summary>
    /// Pulls the player toward an anchor. Called by VineGrapple, which owns the target and the
    /// input; this only owns the CharacterController, the same split PlayerMovement already
    /// keeps with everything else that throws the player around.
    /// </summary>
    /// <summary>
    /// Retuned 2026-08-22 from a MoveTowards chase to an actual force, per direct request for
    /// something more physics-based - harder to control, more rewarding to use well. The old
    /// version smoothly steered the whole velocity vector, y-component included, straight at the
    /// anchor every frame - which meant gravity was never actually fighting it and your own
    /// momentum going in never mattered, since it just got overwritten toward the "correct"
    /// direction regardless of where you already were. It was reliable because there was nothing
    /// to actually manage.
    ///
    /// Gravity now stays on, and the pull is added to whatever velocity you already had rather
    /// than replacing it - so a grapple fired while already moving fast in some other direction
    /// curves rather than snapping straight to the anchor line, and pulling toward something
    /// above you costs real work against your own weight instead of being free. The speed cap
    /// still holds, so it can't run away, but reaching it now depends on how well the throw lines
    /// up with where you were already headed rather than being guaranteed every time.
    /// </summary>
    public void Grapple(Vector3 anchor, float accel, float maxSpeed, float dt)
    {
        Vector3 toAnchor = anchor - transform.position;
        Vector3 dir = toAnchor.sqrMagnitude > 0.01f ? toAnchor.normalized : transform.forward;

        velocity.y -= gravity * dt;
        velocity += dir * accel * dt;

        if (velocity.magnitude > maxSpeed)
            velocity = velocity.normalized * maxSpeed;

        grounded = false;

        controller.Move(velocity * dt);
    }

    [Header("Slide and crouch")]
    [Tooltip("Speed you must already be carrying for the slide key to slide rather than crouch. "
             + "Below it you simply go down.")]
    [SerializeField] float slideEntrySpeed = 6.5f;

    [Tooltip("Multiplier applied to your speed the moment a slide starts. Retuned 2026-08-21: "
             + "1.18 was so small that slideDrag ate it within a second and a slide covered less "
             + "ground than just running the same stretch. At 1.5, entering from max run (8.13) "
             + "kicks to ~12.2.")]
    [SerializeField] float slideKick = 1.5f;

    [Tooltip("How fast a slide bleeds off, in metres per second per second. Lower slides "
             + "further. Paired with slideKick and slideExitSpeed so the average speed across a "
             + "slide's whole duration comes out above maxGroundSpeed - a slide that averages "
             + "below running speed is a worse way to cross the same ground, which is what the "
             + "old 7/3.5 pairing did.")]
    [SerializeField] float slideDrag = 5.5f;

    [Tooltip("Below this a slide has run out and becomes a crouch. Raised from 3.5 alongside the "
             + "kick and drag retune - a slide now ends at a solid jog rather than a crawl, which "
             + "is also what keeps the average speed across the slide above running pace.")]
    [SerializeField] float slideExitSpeed = 5.5f;

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
             + "taking a frame of friction. One means a perfect hop costs nothing.\n\n"
             + "Retuned from 1 on 2026-08-22 - reported as gaining WAY too much speed WAY too "
             + "quickly. Root cause was a side effect of the same-day fix for the slide-hop "
             + "redirect complaint: airSpeedCap went from 0.762 to 2.5 so air-strafing could "
             + "meaningfully steer a hop, but that also means *every* jump, not just a slide-hop "
             + "one, can gain up to 2.5 m/s a hop from ordinary strafing - and at bhopKeep 1, a "
             + "perfect landing lost nothing to offset it, so a plain jump-jump-jump chain with "
             + "no sliding at all compounded without limit. This only touches a landing that isn't "
             + "entering a slide (UpdateStance already routes a fast landing into the slide's own "
             + "chain/exhaustion system instead), so the slide-hop redirect fix itself is untouched "
             + "- this is pure-bhop specifically.")]
    [Range(0.5f, 1f)] [SerializeField] float bhopKeep = 0.92f;

    [Header("Wall run")]
    [Tooltip("Fraction of normal gravity while wall running. Not zero - full weightlessness reads "
             + "as flying, not running; a small pull down is what keeps it feeling like a wall "
             + "under your feet rather than a stopped clock.")]
    [Range(0.05f, 0.6f)] [SerializeField] float wallRunGravityScale = 0.22f;

    [SerializeField] float wallRunRise = 1.2f;
    [SerializeField] float wallRunMaxSeconds = 1.25f;
    [SerializeField] float wallRunCheckDistance = 0.8f;
    [SerializeField] float wallJumpAway = 5.5f;
    [SerializeField] float wallJumpUp = 6.4f;

    [Tooltip("Cooldown after leaving a wall run before another one can start, so jumping off "
             + "doesn't just re-latch the same wall a frame later.")]
    [SerializeField] float wallRunReentryDelay = 0.4f;

    bool wallRunning;
    Vector3 wallNormal;
    float wallRunEndsAt;
    float wallRunBlockedUntil = -99f;

    /// Whether a wall run is currently active. Public the same way Sliding/Crouching are, so the
    /// camera can lean toward the wall while it's happening.
    public bool WallRunning => wallRunning;
    public Vector3 WallNormal => wallNormal;

    [Header("Air brake and ground slam")]
    [Tooltip("One key, two moves, told apart by which way you're already going rather than by "
             + "timing a tap against a hold - pressing it while rising or at the apex brakes, "
             + "pressing it while already falling slams. Either way is instant: no wait-and-see "
             + "delay to tell a tap from a hold would have cost the fast one its whole point.")]
    [SerializeField] float airBrakeKeep = 0.12f;

    [SerializeField] float airBrakeMinSpeed = 3f;
    [SerializeField] float groundSlamSpeed = 19f;

    [Tooltip("Seconds after landing a slam before another one can fire. Reported as spammable "
             + "with no cooldown at all - jump, slam, land, jump, slam again, as fast as you can "
             + "press the key.")]
    [SerializeField] float groundSlamCooldown = 1.4f;

    float groundSlamCooldownUntil = -99f;
    bool slamming;

    CharacterController controller;
    Vector3 velocity;
    bool grounded;
    float jumpPressedAt = -1f;

    [Tooltip("How many slides in a row keep paying. After this the chain gives nothing until "
             + "you break it, so the technique has a ceiling rather than being infinite speed.")]
    [SerializeField] int maxChain = 4;

    [Tooltip("Extra kick per link in the chain, on top of the base slide boost. Lowered from 0.09 "
             + "on 2026-08-22 - a full 4-chain compounds this multiplicatively on top of "
             + "slideJumpBoost's flat add between every link, and a clean chain was reported "
             + "back as 'too fast if you get it right', annoying to play against. Chain 4's kick "
             + "is now 1.68 against the old 1.77 - still the highest link, just not by as much.")]
    [SerializeField] float chainBonus = 0.06f;

    [Tooltip("Seconds you have after leaving a slide to start the next one and keep the chain.")]
    [SerializeField] float chainWindow = 0.9f;

    [Tooltip("Speed you get when you jump straight out of a slide, on top of what you had. Raised "
             + "from 1.6 to 2.4 on 2026-08-21 because the boost wasn't paying off against the "
             + "slide retune's bigger numbers - overshot it. Reported 2026-08-22 as part of why a "
             + "full chain gets 'too fast if you get it right', since this adds flat on every one "
             + "of up to three jump-outs in a chain, on top of chainBonus's own compounding. "
             + "Split the difference at 1.9 rather than reverting outright - still more than the "
             + "original 1.6, just not stacking as hard three times in a row.")]
    [SerializeField] float slideJumpBoost = 1.9f;

    [Tooltip("Seconds you cannot slide for after topping out the chain.")]
    [SerializeField] float exhaustion = 10f;

    [Tooltip("How long before landing a slide press still counts, in seconds. This is what makes "
             + "chaining possible - you press it in the air and it fires on touchdown.")]
    [SerializeField] float slideBuffer = 0.22f;

    [Header("Fatigue")]
    [Tooltip("Added on every slide entry, whether or not it's part of a chain. Separate from the "
             + "chain counter, which resets after chainWindow - this doesn't, at least not "
             + "quickly, which is the whole point of it.")]
    [SerializeField] float fatiguePerSlide = 1f;

    [Tooltip("Fatigue that triggers exhaustion, the same lockout topping out the chain gives. "
             + "Set just above maxChain so one clean 4-chain still exhausts through the chain "
             + "path first, and this one only fires for the pattern that path can't see.")]
    [SerializeField] float fatigueLimit = 5f;

    [Tooltip("Fatigue lost per second, all the time, not only while grounded. Slow on purpose: "
             + "recovering from a full 3-slide burst (fatigue 3) takes 8 seconds at this rate "
             + "(0.375/s - tuned directly to that number rather than the other way round), so "
             + "waiting out chainWindow's 0.9s between bursts barely dents it. Reported "
             + "2026-08-22: slide three times, wait a few seconds, slide three times, repeat, "
             + "and the chain-only exhaustion check never fires because it never actually hits "
             + "the chain ceiling - each burst starts a fresh chain from 1. That's what this "
             + "field exists to close: fatigue accumulates across bursts even when the chain "
             + "itself keeps resetting, so a few seconds of waiting stops being enough.")]
    [SerializeField] float fatigueDecay = 0.375f;

    int chain;
    float chainExpires = -99f;
    float exhaustedUntil = -99f;
    float slidePressedAt = -99f;
    float fatigue;

    /// Whether sliding is currently locked out, and for how much longer. Both public because the
    /// HUD has to be able to say so - being unable to slide with no explanation is the worst
    /// possible version of this.
    public bool Exhausted => Time.time < exhaustedUntil;
    public float ExhaustedFor => Mathf.Max(0f, exhaustedUntil - Time.time);

    /// Cleared on respawn. Dying is enough of a reset.
    public void ClearExhaustion()
    {
        exhaustedUntil = -99f;
        chain = 0;
        chainExpires = -99f;
        fatigue = 0f;
    }

    /// How many slides deep the current chain is, for the camera and the effects. Zero when
    /// nothing is going on.
    public int SlideChain => Time.time < chainExpires || Exhausted ? chain : 0;

    bool sliding;
    bool crouching;
    float standingHeight;
    float standingCentre;
    float landedAt = -99f;

    /// Whether the player is currently sliding. Read by the camera, which leans into it, and by
    /// anything that wants to know why somebody is moving faster than they should be.
    public bool Sliding => sliding;
    public bool Crouching => crouching;

    /// 1 when standing, down to `crouchHeight` while crouched or sliding, eased the same way the
    /// capsule itself is - so a camera reading this drops into a slide instead of snapping down
    /// with it. Added 2026-08-22 for exactly that: nothing was reading the capsule's own eased
    /// height at all, so the camera never moved while sliding despite the collider genuinely
    /// shrinking underneath it.
    public float StanceFraction => standingHeight > 0.01f ? controller.height / standingHeight : 1f;

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

        // VineGrapple owns velocity and the CharacterController for as long as this is true - it
        // calls Grapple() itself, on its own Update, rather than this one computing anything for
        // it. Everything below this line (slide, crouch, jump buffering, ground/air move) is
        // exactly the WASD-and-gravity game, and none of it should run while being reeled in.
        if (Grappling)
            return;

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
        {
            landedAt = Time.time;

            if (slamming)
            {
                SlamLandingEffects();
                slamming = false;
                groundSlamCooldownUntil = Time.time + groundSlamCooldown;
            }

            if (wallRunning)
                EndWallRun(false);
        }

        UpdateStance(listening, dt);
        UpdateAirAction(listening);

        Vector3 wishDir = WishDirection();

        float wishSpeed = (crouching ? crouchSpeed : maxGroundSpeed) * AdrenalineSpeed;

        if (grounded)
        {
            GroundMove(wishDir, wishSpeed, wantsJump, dt);
        }
        else if (!UpdateWallRun(listening, wantsJump, dt))
        {
            AirMove(wishDir, wishSpeed, dt);
        }

        UpdateWallRunScrape();

        // The one place total speed is actually bounded, rather than just how often something is
        // allowed to add more of it. Vertical is untouched - this is about bhop and slide chains
        // running away horizontally, not about falling.
        Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
        if (flatVelocity.sqrMagnitude > maxHorizontalSpeed * maxHorizontalSpeed)
        {
            flatVelocity = flatVelocity.normalized * maxHorizontalSpeed;
            velocity.x = flatVelocity.x;
            velocity.z = flatVelocity.z;
        }

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
        // Decays every frame regardless of what else is happening, which is what makes it a
        // fatigue meter rather than a second chain counter - it doesn't care whether you're
        // mid-slide, standing still, or in the air, only how long it's been since you last added
        // to it.
        fatigue = Mathf.Max(0f, fatigue - fatigueDecay * dt);

        bool held = !listening && KeyBinds.Held(KeyBinds.Action.Walk);

        // Remembered on the press, so hitting slide just before you touch down still slides when
        // you land. Without this, chaining was impossible by design: you have to press it while
        // airborne to catch the landing, and the check only ever looked at whether you were
        // holding it on a frame where you happened to already be on the floor.
        //
        // Only re-armed while not already sliding. A press that lands while sliding is already
        // is not a queue for anything - you're already in the state it would ask for - and
        // arming it anyway meant a slide that ended from a natural speed drop while the key was
        // still held (or freshly re-tapped) could look like a landing queue and refire itself.
        if (!listening && !sliding && KeyBinds.Pressed(KeyBinds.Action.Walk))
            slidePressedAt = Time.time;

        bool buffered = Time.time - slidePressedAt <= slideBuffer;
        bool wants = held || buffered;

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
        else if (!sliding && !crouching && grounded)
        {
            // Grounded is now part of the gate itself, not just the branch inside it. Queuing a
            // slide by pressing the key while still airborne used to fall straight into the
            // "else" below and crouch immediately, mid-air, before landing had even happened -
            // so by the time you actually touched down you were already crouched rather than
            // still waiting to decide, and the slide-entry check here never got a chance to run
            // on the landing frame at all. That was the whole jump-into-slide jank: queuing a
            // slide silently turned into queuing a crouch instead.
            //
            // Which one you get is decided once, now that we know you're on the ground, by how
            // fast you were already going. Deciding it every frame would flicker between the two
            // at the boundary.
            if (speed >= slideEntrySpeed && !Exhausted)
            {
                sliding = true;
                slidePressedAt = -99f;

                // Chaining. Each slide taken shortly after the last one pays a little more, up
                // to a ceiling - so slide, hop, slide, hop builds speed and is worth learning,
                // but it tops out rather than turning into infinite acceleration. Break the
                // rhythm and you start again from the base kick.
                chain = Time.time < chainExpires ? chain + 1 : 1;
                chainExpires = Time.time + chainWindow;

                // Past the ceiling you are spent. Ten seconds of no sliding at all, which is
                // long enough to be a real cost and short enough that it is not a punishment -
                // and it is the thing that stops the chain being infinite speed.
                if (chain >= maxChain)
                {
                    exhaustedUntil = Time.time + exhaustion;
                    chain = maxChain;
                }

                // The slower-decaying half of the same idea, for the pattern the chain ceiling
                // can't see: several short bursts, each one broken deliberately before it ever
                // reaches maxChain, with just enough of a gap between them for the chain to
                // reset but not enough for fatigue to meaningfully recover. Same lockout, same
                // cost, triggered by total recent sliding rather than by one unbroken run of it.
                fatigue += fatiguePerSlide;

                if (fatigue >= fatigueLimit)
                {
                    exhaustedUntil = Time.time + exhaustion;
                    fatigue = 0f;
                }

                float kick = slideKick + chainBonus * (chain - 1);

                // The kick is what makes sliding a decision rather than a brake. Applied to the
                // direction you are already travelling, not the one you are looking - a slide
                // goes where you were going.
                //
                // Retuned 2026-08-22 - reported as "you can QUICKLY get so much speed", and the
                // reason was this multiplying whatever velocity you already carried into the
                // slide rather than a stable reference. Chain 1 out of a normal run is fine
                // (1.5x of ~8 m/s is the intended kick), but chain 2 was 1.56x of chain 1's
                // *already boosted* result, chain 3 was 1.62x of that, and so on - each link
                // compounding on the last one's compounding. Three or four chained slide-hops
                // reached the safety ceiling in a couple of seconds. Multiplying baseline ground
                // speed instead of current speed, then adding that as a flat bonus, reproduces
                // the exact same chain-1 number (current speed happens to equal baseline there)
                // while every extra link adds a bounded amount instead of re-multiplying a stack.
                // Reuses `speed`, already computed above as this same velocity's magnitude - nothing
                // between there and here touches velocity, so recomputing it under a new name would
                // just be the same number twice.
                Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);

                if (speed > 0.01f)
                {
                    float baseline = maxGroundSpeed * AdrenalineSpeed;
                    float boosted = speed + baseline * (kick - 1f);
                    flat = flat.normalized * boosted;

                    velocity.x = flat.x;
                    velocity.z = flat.z;
                }

                // No sound triggered here any more - removed 2026-08-22. This fired a full ~1.8s
                // one-shot off the same long Slide clips SpeedRush's continuous scrape source
                // already loops, on every single entry, completely independent of how long the
                // slide actually lasted. A quick slide-hop chain could easily be shorter than
                // 1.8s, so the one-shot kept audibly running after the slide had already ended -
                // exactly the bug SpeedRush.UpdateScrape's own doc comment already warns against
                // ("a sustained sound wants a sustained source"), just reintroduced from a
                // different call site. It also meant every slide, all match, only ever played
                // one of two clips chosen once at startup and never varied, which read as static
                // and repetitive on top of the overlap. The "rising pitch with the chain" feedback
                // this was for still exists - SpeedRush now sets it on the loop itself, which it
                // can actually control, instead of firing a second, independent sound alongside it.
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

    /// <summary>
    /// Hold the slide/crouch key near a wall while airborne, and stick to it for as long as it's
    /// held.
    ///
    /// Redesigned 2026-08-22, reported as "really weird" - the original auto-latched onto any
    /// wall you were moving toward fast enough, with no button, which made it unpredictable to
    /// start and impossible to end on purpose. Direct request was explicit: press the key near a
    /// wall to start, hold it to stay on, let go and you fall immediately. That's what this is -
    /// no speed threshold, no "moving toward it" check, just proximity plus the key. Returns
    /// whether it owned this frame's velocity, so Update() knows whether to fall back to AirMove.
    ///
    /// While active, gravity is scaled down rather than removed - full weightlessness reads as
    /// flying, and the wall stops being a wall - and velocity is clipped to the wall's own
    /// tangent plane the same way a wall collision already is elsewhere, just every frame instead
    /// of only on contact, so steering along it doesn't slowly drift back into the wall or away
    /// from it. A jump is the one other way out, and the only one with a payoff - it pushes away
    /// from the wall as well as up, rather than just dropping you.
    /// </summary>
    bool UpdateWallRun(bool listening, bool wantsJump, float dt)
    {
        bool holding = !listening && KeyBinds.Held(KeyBinds.Action.Walk);

        if (wallRunning)
        {
            if (!holding)
            {
                EndWallRun(false);
                return false;
            }

            if (wantsJump)
            {
                EndWallRun(true);
                return false;
            }

            bool stillWall = Physics.Raycast(transform.position, -wallNormal,
                                             wallRunCheckDistance + 0.15f, Hitbox.WorldMask,
                                             QueryTriggerInteraction.Ignore);

            if (!stillWall || Time.time >= wallRunEndsAt)
            {
                EndWallRun(false);
                return false;
            }

            velocity -= wallNormal * Vector3.Dot(velocity, wallNormal);
            velocity.y = Mathf.Lerp(velocity.y, wallRunRise, 1f - Mathf.Exp(-6f * dt));
            velocity.y -= gravity * wallRunGravityScale * dt;

            WallRunTickEffects(dt);
            return true;
        }

        if (!holding || Time.time < wallRunBlockedUntil)
            return false;

        Vector3 right = transform.right;
        RaycastHit hit;

        if (Physics.Raycast(transform.position, right, out RaycastHit rHit,
                            wallRunCheckDistance, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
        {
            hit = rHit;
        }
        else if (Physics.Raycast(transform.position, -right, out RaycastHit lHit,
                                 wallRunCheckDistance, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
        {
            hit = lHit;
        }
        else
        {
            return false;
        }

        // Only a genuine wall - near horizontal normal. A steep ramp or a low ceiling shouldn't
        // start one just because the raycast happened to clip it.
        if (Mathf.Abs(hit.normal.y) > 0.3f)
            return false;

        wallRunning = true;
        wallNormal = hit.normal;
        wallRunEndsAt = Time.time + wallRunMaxSeconds;
        wallRunDustTimer = 0f;

        WallRunStartEffects();
        return true;
    }

    void EndWallRun(bool jumpOff)
    {
        wallRunning = false;
        wallRunBlockedUntil = Time.time + wallRunReentryDelay;

        if (jumpOff)
        {
            velocity += wallNormal * wallJumpAway;
            velocity.y = wallJumpUp;
            jumpPressedAt = -99f;

            WallJumpEffects();
        }
    }

    /// <summary>
    /// Air brake and ground slam, on separate keys as of 2026-08-22.
    ///
    /// They used to share Walk, told apart by vertical velocity - falling meant slam, rising
    /// meant brake - which broke the moment it met the slide buffer, which *also* reads Walk
    /// while airborne to remember a slide for landing. Landing is always falling, so trying to
    /// buffer a slide kept firing a slam instead, every time. Ground pound now has its own key
    /// (`KeyBinds.Action.GroundPound`) and doesn't touch Walk at all. The air brake stays on Walk
    /// but keeps its rising-only condition - it never conflicted with the slide buffer in the
    /// first place, since you can't be about to land while still going up.
    /// </summary>
    void UpdateAirAction(bool listening)
    {
        if (listening || grounded || wallRunning || Grappling)
            return;

        if (KeyBinds.Pressed(KeyBinds.Action.GroundPound) && Time.time >= groundSlamCooldownUntil)
            GroundSlam();

        if (KeyBinds.Pressed(KeyBinds.Action.Walk) && velocity.y >= 0f)
            AirBrake();
    }

    void AirBrake()
    {
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;

        if (speed < airBrakeMinSpeed)
            return;

        Vector3 direction = flat / speed;

        velocity.x *= airBrakeKeep;
        velocity.z *= airBrakeKeep;

        AirBrakeEffects(direction);
    }

    void GroundSlam()
    {
        velocity.y = -groundSlamSpeed;
        slamming = true;

        GroundSlamStartEffects();
    }

    // ---------------------------------------------------------------- movement tech effects
    //
    // One shared burst rather than four near-identical ParticleSystem setups - vault dust, a wall
    // run's scuff, an air brake's kicked-back debris and a slam's landing ring only differ in the
    // numbers passed in. Real particles with real velocity throughout, not a static billboard
    // that fades in place - see BulletDecal.cs's own note on why that specifically reads as weak.

    static Material moveBurstMaterial;
    static Sprite[] moveBurstShapes;
    float wallRunDustTimer;

    static Sprite MoveBurstShape(string prefix)
    {
        if (moveBurstShapes == null)
            moveBurstShapes = Resources.LoadAll<Sprite>("Particles/Boom");

        if (moveBurstShapes.Length == 0)
            return null;

        foreach (Sprite s in moveBurstShapes)
        {
            if (s.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return s;
        }

        return moveBurstShapes[0];
    }

    static Material MoveBurstMaterial()
    {
        if (moveBurstMaterial != null)
            return moveBurstMaterial;

        Shader shader = Shader.Find("Particles/Additive")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        moveBurstMaterial = new Material(shader) { name = "~moveBurst", enableInstancing = true };
        return moveBurstMaterial;
    }

    static void MovementBurst(Vector3 point, Vector3 direction, Color tint, string spritePrefix,
                             int count, float coneAngle, float speedMin, float speedMax,
                             float sizeMin, float sizeMax, float life, float gravityScale)
    {
        Sprite sprite = MoveBurstShape(spritePrefix);
        if (sprite == null)
            return;

        GameObject host = new GameObject("~moveFx");
        host.transform.position = point;
        host.transform.rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction)
            : Quaternion.identity;

        ParticleSystem ps = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = tint;
        main.gravityModifier = gravityScale;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = 0.02f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = MoveBurstMaterial();
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        view.SetPropertyBlock(block);

        ps.Play();
    }

    static readonly Color DustTint = new Color(0.72f, 0.66f, 0.5f);

    void AirBrakeEffects(Vector3 backward)
    {
        Juice.Shake(0.3f);
        MovementBurst(transform.position, -backward, Color.white, "star", 6, 40f, 1.5f, 3f,
                     0.03f, 0.06f, 0.16f, 0.3f);

        // Footstep, not Slide - reported as sounding the same as an actual slide, which this
        // shouldn't, being the opposite of one (a hard stop rather than a sustained scrape).
        // Pitched sharply up and treated as a single hit rather than the looping scrape SpeedRush
        // owns, so it reads as a skid rather than a slide.
        GameAudio.PlayShaped(GameAudio.AirBrake, 0.5f, 1.6f, GameAudio.Footstep, 1.8f);
    }

    void GroundSlamStartEffects()
    {
        GameAudio.PlayShaped(GameAudio.Slam, 0.4f, 1f, GameAudio.Vine, 0.7f);
    }

    void SlamLandingEffects()
    {
        Juice.Hit(0.55f);

        // Feet, not the capsule's own pivot - CharacterController.bounds already accounts for
        // center/height correctly, which transform.position alone does not. A burst at the
        // capsule's centre lands roughly at chest height instead of on the ground, which is
        // likely why this was reported as having no particle effect at all - it was there, just
        // floating at head height instead of reading as a landing impact.
        Vector3 feet = new Vector3(transform.position.x, controller.bounds.min.y, transform.position.z);

        MovementBurst(feet, Vector3.up, DustTint, "circle", 10, 60f, 2f, 5f,
                     0.05f, 0.12f, 0.25f, 0.8f);
        GameAudio.PlayShaped(GameAudio.Slam, 0.7f, 0.8f, GameAudio.Explosion, 0.55f);
    }

    void WallRunStartEffects()
    {
        MovementBurst(transform.position - wallNormal * 0.15f, wallNormal, DustTint, "spark",
                     3, 30f, 0.4f, 1f, 0.02f, 0.04f, 0.12f, 0.3f);
    }

    // Dust only - the audio half of running a wall moved to a continuous loop
    // (UpdateWallRunScrape) instead of a one-shot fired every tick, which read as choppy machine-
    // gunning of the same clip rather than a sustained sound. Kept as its own method rather than
    // folded into the loop's own update, since dust wants a fixed cadence and volume wants to
    // track speed/state continuously - two different things happening to share one timer badly.
    void WallRunTickEffects(float dt)
    {
        wallRunDustTimer -= dt;

        if (wallRunDustTimer > 0f)
            return;

        wallRunDustTimer = 0.14f;
        MovementBurst(transform.position - wallNormal * 0.15f, wallNormal, DustTint, "spark",
                     2, 25f, 0.3f, 0.8f, 0.02f, 0.04f, 0.1f, 0.3f);
    }

    void WallJumpEffects()
    {
        Juice.Shake(0.25f);
        GameAudio.PlayShaped(GameAudio.WallRun, 0.45f, 1.4f, GameAudio.Vine, 1.2f);
    }

    AudioClip[] wallRunClips;
    AudioSource wallRunScrape;
    bool wasWallRunning;
    float wallRunAudioSeed;

    /// <summary>
    /// A continuous loop for as long as a wall run lasts, the same shape `SpeedRush` already uses
    /// for the slide scrape - attack and release at different rates, a bit of Perlin wobble so a
    /// long run doesn't sit at one dead-flat pitch. Replaced a one-shot fired every 0.14s, which
    /// read as the same clip machine-gunning rather than a sustained sound, and which fell back to
    /// the `Slide` bank - reported as sounding like an actual slide, which a wall run very much
    /// isn't. Called every frame regardless of `wallRunning` so the volume can release smoothly
    /// on the frame it ends, the same reason `SpeedRush.UpdateScrape` isn't gated either.
    /// </summary>
    void UpdateWallRunScrape()
    {
        if (wallRunClips == null)
        {
            wallRunClips = Resources.LoadAll<AudioClip>("Audio/" + GameAudio.WallRun);

            if (wallRunClips.Length == 0)
                wallRunClips = Resources.LoadAll<AudioClip>("Audio/" + GameAudio.Vine);
        }

        if (wallRunClips.Length == 0)
            return;

        if (wallRunScrape == null)
        {
            wallRunScrape = gameObject.AddComponent<AudioSource>();
            wallRunScrape.loop = true;
            wallRunScrape.playOnAwake = false;
            wallRunScrape.spatialBlend = 0f;
            wallRunScrape.volume = 0f;
            wallRunAudioSeed = Random.Range(0f, 100f);
        }

        if (wallRunning && !wasWallRunning)
        {
            wallRunScrape.clip = wallRunClips[Random.Range(0, wallRunClips.Length)];
            wallRunScrape.Stop();
            wallRunScrape.Play();
        }

        wasWallRunning = wallRunning;

        float wanted = wallRunning ? 0.42f * GameSettings.SfxVolume : 0f;
        float rate = wanted > wallRunScrape.volume ? 10f : 22f;
        wallRunScrape.volume = Mathf.MoveTowards(wallRunScrape.volume, wanted, Time.deltaTime * rate);

        float wobble = (Mathf.PerlinNoise(wallRunAudioSeed, Time.time * 1.6f) - 0.5f) * 0.1f;
        wallRunScrape.pitch = 0.95f + wobble;

        if (wallRunScrape.volume > 0.001f && !wallRunScrape.isPlaying)
            wallRunScrape.Play();
        else if (wallRunScrape.volume <= 0.001f && wallRunScrape.isPlaying)
            wallRunScrape.Stop();
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

    /// <summary>
    /// Clips velocity against whatever wall the controller just hit.
    ///
    /// Added 2026-08-22 - nothing anywhere in this file ever removed speed on a collision.
    /// `controller.Move()` stops your *position* at a wall, but the `velocity` field driving it
    /// was never told that happened, so it kept its full pre-collision magnitude regardless -
    /// reported as "hit a wall at speed, and as long as I keep holding forward and turn slightly
    /// left or right, all that speed is still there and I just carry on." Unity calls this once
    /// per collider actually touched during that Move(), so a corner can clip against two walls
    /// in the same frame.
    ///
    /// Only clips near-vertical surfaces - a wall, not a floor or ceiling. Floors are already
    /// handled by `grounded` and the small downward push in GroundMove; clipping vertical
    /// velocity against a floor's own normal here would fight that and make landings inconsistent.
    ///
    /// Removes only the component of velocity pointing *into* the surface (the classic Quake
    /// `ClipVelocity`), not the whole vector - a graze along a wall keeps its tangential speed,
    /// which is correct wall-sliding, not a bug. A square, head-on hit has almost all of its
    /// velocity pointing into the normal, so it comes out of this near zero, which is the actual
    /// fix: momentum dies on impact instead of surviving for free the moment you twitch the
    /// mouse.
    /// </summary>
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Mathf.Abs(hit.normal.y) > 0.3f)
            return;

        float into = Vector3.Dot(velocity, hit.normal);

        if (into < 0f)
            velocity -= hit.normal * into;
    }
}
