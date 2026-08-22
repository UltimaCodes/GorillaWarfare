using UnityEngine;
using Photon.Pun;

/// <summary>
/// The vine grapple: latch onto a vantage point or an enemy and get pulled there fast.
///
/// Planned in ideas.md as closer to Attack on Titan's omnidirectional mobility gear than
/// the original world-geometry-only version of that entry. Built 2026-08-21.
///
/// Present on every player, owner and remote copies alike - unlike PlayerMovement or SpeedRush,
/// which only ever exist on the local body. The rope has to be visible to whoever is watching,
/// even though only the owner ever fires one or gets pulled by one, so this gates its own input
/// internally the same way FootstepPlayer and MonkeyRig already do rather than being added
/// conditionally in PlayerController.
///
/// Not a weapon slot. Fires on its own key and the tradeoff for using it is temporal rather than
/// an inventory cost - your gun is unusable for as long as you're attached, enforced directly in
/// PlayerController's input handling rather than by occupying a loadout entry.
///
/// Hold to fire and stay attached; release to let go early. Reaching the anchor, the anchor
/// becoming invalid (the player it was latched to disconnects, for instance), or a safety
/// timeout all detach the same way a release does.
/// </summary>
[RequireComponent(typeof(PhotonView))]
public class VineGrapple : MonoBehaviour
{
    [Header("Reach")]
    [Tooltip("How far a vine can latch onto something, in metres.")]
    [SerializeField] float maxRange = 40f;

    [Tooltip("Radius of the cast used to find something to latch onto, in metres. A bare raycast "
             + "made this feel unreliable - a small anchor point at range is a tiny target for a "
             + "single ray, and a miss looked identical to the key not registering at all. "
             + "Raised from 0.6 on 2026-08-22, still reported as unreliable at that size - "
             + "SphereCastAll now also picks whichever candidate is nearest the crosshair rather "
             + "than whatever the first single cast happened to hit, which matters more as this "
             + "grows and starts sweeping past more than one thing.")]
    [SerializeField] float castRadius = 1.1f;

    [Tooltip("How close counts as arrived - ends the pull on a vantage point, or lands the hit "
             + "on an enemy.")]
    [SerializeField] float arriveDistance = 1.6f;

    [Header("Pull")]
    [Tooltip("Metres per second squared toward the anchor. Higher than any ground or air "
             + "acceleration in the game on purpose - this is meant to read as the fastest way "
             + "to gain speed, the way the brief asked for. Raised from 60 on 2026-08-22 - the "
             + "most loved feature so far, reported back that the pull itself felt slow to "
             + "actually take hold even after maxPullSpeed came down to something reasonable. At "
             + "110, reaching that 24 m/s ceiling from a standing start takes about 0.22s against "
             + "the old 0.4s - the top speed itself hasn't changed, only how fast the vine gets "
             + "you there.")]
    [SerializeField] float pullAccel = 110f;

    [Tooltip("Speed the pull chases, in metres per second. Retuned down from 40 on 2026-08-22 - "
             + "reported as way too fast, and the numbers back that up: a full slide chain tops "
             + "out around 14, so 40 was close to five times running speed rather than a clear "
             + "step above the game's other movement tech. 24 sits above a good slide chain "
             + "without dwarfing everything else the way the old number did.")]
    [SerializeField] float maxPullSpeed = 24f;

    [Tooltip("Safety cutoff, in seconds. Covers an anchor that's technically still reachable but "
             + "never actually gets any closer - circling around geometry, for instance.")]
    [SerializeField] float maxDuration = 3.5f;

    [Header("Damage")]
    [Tooltip("Base damage on contact with an enemy, before the same speed scaling every "
             + "momentum hit gets - see PlayerMovement.MomentumDamage.")]
    [SerializeField] float contactDamage = 40f;

    [Header("Visual")]
    [SerializeField] float lineWidth = 0.05f;
    [SerializeField] Color vineColour = new Color(0.30f, 0.46f, 0.16f);

    static Material sharedMaterial;

    PlayerController player;
    PhotonView PV;
    LineRenderer line;

    [Tooltip("Minimum seconds between grapple attempts. Added 2026-08-22 so the key can't be "
             + "spammed - without it there was nothing stopping a press every frame, each one "
             + "cancelling the last whiff sound before it finished and firing a fresh cast.")]
    [SerializeField] float attemptCooldown = 0.5f;

    int anchorViewID = -1;
    Vector3 anchorWorldPoint;
    float attachedAt;
    float lastAttemptAt = -99f;
    bool hitLandedThisAttach;

    /// Whether this copy - owner or remote - is currently shown attached. Read by
    /// PlayerController to lock the trigger while grappling.
    public bool Attached { get; private set; }

    void Awake()
    {
        player = GetComponent<PlayerController>();
        PV = GetComponent<PhotonView>();
    }

    void Update()
    {
        if (player == null || PV == null)
            return;

        // Only the owner reads input, moves the player, or resolves a hit - the same
        // client-authoritative split every other piece of combat in this game already uses.
        // Everybody, owner included, draws the rope off the same replicated Attached/anchor
        // state, which is what keeps every client's line in the same place without streaming it.
        if (PV.IsMine)
            UpdateOwner();

        UpdateLine();
    }

    void UpdateOwner()
    {
        // The settings screen owns the keyboard while it's open, same as every other input
        // PlayerController itself reads.
        bool listening = SettingsMenu.IsOpen;

        if (!Attached)
        {
            if (!listening && KeyBinds.Pressed(KeyBinds.Action.Grapple)
                && Time.time - lastAttemptAt >= attemptCooldown)
            {
                lastAttemptAt = Time.time;
                TryAttach();
            }

            return;
        }

        if (listening || !KeyBinds.Held(KeyBinds.Action.Grapple))
        {
            Detach();
            return;
        }

        if (Time.time - attachedAt > maxDuration)
        {
            Detach();
            return;
        }

        Vector3 anchor = ResolveAnchor(out bool valid, out PlayerController targetPlayer);

        if (!valid)
        {
            Detach();
            return;
        }

        if (Vector3.Distance(transform.position, anchor) <= arriveDistance)
        {
            if (targetPlayer != null && !hitLandedThisAttach)
                LandHit(targetPlayer);

            // Small on purpose, same as the throw - this is a landing, not a kill, even on the
            // attach where it happens to also be one. The kill's own weight already comes from
            // TakeDamage's normal feedback (the hit sound in LandHit, the death pipeline); this
            // is only ever the "you arrived" beat, and only ever reachable while still holding
            // the button, since letting go detaches through the branch above this one instead.
            Juice.Hit(0.4f);

            Detach();
            return;
        }

        // PlayerMovement only exists on the owner's own copy, and is rebuilt on every respawn -
        // fetched live rather than cached, the same way PlayerController.Launch already does for
        // exactly the same reason.
        PlayerMovement movement = GetComponent<PlayerMovement>();

        if (movement != null)
        {
            movement.Grappling = true;
            movement.Grapple(anchor, pullAccel, maxPullSpeed, Time.deltaTime);
        }
    }

    /// <summary>
    /// Casts from the camera and decides what got hit, if anything.
    ///
    /// Uses the same mask shape SingleShotGun's own trace uses - everything except the movement
    /// capsule layer, so the ray lands on a hitbox or on the world, never on the collider the
    /// shooter is standing inside.
    ///
    /// SphereCastAll rather than a single SphereCast, and the winner is whichever candidate the
    /// ray passes nearest to rather than whichever the physics engine happens to report first -
    /// "the nearest thing on your crosshair" means angularly nearest to where you're actually
    /// looking, not just the first thing a fat ray glanced.
    /// </summary>
    void TryAttach()
    {
        Camera camera = PlayerController.LocalCamera;

        if (camera == null)
            return;

        int mask = TargetMask();
        Ray ray = new Ray(camera.transform.position, camera.transform.forward);

        RaycastHit[] hits = Physics.SphereCastAll(ray, castRadius, maxRange, mask,
                                                  QueryTriggerInteraction.Ignore);

        int bestIndex = -1;
        float bestAngle = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            // Can't latch onto your own hitbox - the camera sits inside them, so a wide enough
            // cast always finds one.
            if (hits[i].collider.GetComponentInParent<PlayerController>() == player)
                continue;

            float angle = Vector3.Angle(ray.direction, hits[i].point - ray.origin);

            if (angle >= bestAngle)
                continue;

            bestAngle = angle;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            Whiff();
            return;
        }

        RaycastHit hit = hits[bestIndex];
        PlayerController target = hit.collider.GetComponentInParent<PlayerController>();
        int targetViewID = target != null && target.View != null ? target.View.ViewID : -1;

        Begin(targetViewID, hit.point);
    }

    /// <summary>
    /// Nothing was in range. Local only, and deliberately not run through an RPC - a miss isn't
    /// a networked event, it's you finding out your own aim didn't land on anything.
    ///
    /// Existed as a silent no-op originally, which is most of what made the whole mechanic read
    /// as unreliable: pressing G and getting nothing back is indistinguishable from the key not
    /// registering at all. This turns a miss into an audible, different-sounding outcome instead
    /// of the absence of one.
    /// </summary>
    void Whiff()
    {
        GameAudio.PlayPitched(GameAudio.Vine, "swish-13", 0.35f, 0.7f);
    }

    static int TargetMask()
    {
        int player = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        int mask = ~0;

        if (player >= 0)
            mask &= ~(1 << player);

        return mask;
    }

    void Begin(int targetViewID, Vector3 worldPoint)
    {
        PV.RPC(nameof(RPC_Attach), RpcTarget.All, targetViewID, worldPoint);
    }

    void Detach()
    {
        PV.RPC(nameof(RPC_Detach), RpcTarget.All);
    }

    [PunRPC]
    void RPC_Attach(int targetViewID, Vector3 worldPoint)
    {
        Attached = true;
        anchorViewID = targetViewID;
        anchorWorldPoint = worldPoint;
        attachedAt = Time.time;
        hitLandedThisAttach = false;

        // Runs on every client, same as the RPC itself, so the thwip is positional and everyone
        // nearby hears it - only the person who actually fired it also gets stopped for it.
        GameAudio.PlayAt(GameAudio.Vine, transform.position, 0.85f, 0.06f);

        if (PV.IsMine)
            Juice.Hit(0.3f);

        // A latch is a moment, not just a state change - reported back as wanting to actually
        // feel it connect, on top of the thwip and the freeze the throw itself already has. A
        // small spark burst right where it caught, the same Particles/Boom sprites and pattern
        // the bullet impact puff and the grenade's own explosion both already use, just smaller
        // than either - this is a rope catching, not a detonation.
        Puff(worldPoint);
    }

    static Sprite[] boomShapes;

    static void Puff(Vector3 at)
    {
        if (boomShapes == null || boomShapes.Length == 0)
            boomShapes = Resources.LoadAll<Sprite>("Particles/Boom");

        if (boomShapes.Length == 0)
            return;

        Sprite spark = null;
        Sprite core = null;

        foreach (Sprite s in boomShapes)
        {
            if (spark == null && s.name.StartsWith("spark", System.StringComparison.OrdinalIgnoreCase))
                spark = s;
            if (core == null && s.name.StartsWith("circle", System.StringComparison.OrdinalIgnoreCase))
                core = s;
        }

        Color tint = new Color(0.55f, 0.85f, 0.35f, 1f);

        FlashSprite.Spawn(core ?? boomShapes[0], at, 0.12f, 0.26f, 0.09f, tint);

        for (int i = 0; i < 5; i++)
        {
            Vector3 away = Random.onUnitSphere;
            FlashSprite.Spawn(spark ?? boomShapes[0], at + away * 0.06f, 0.05f, 0.01f,
                              Random.Range(0.14f, 0.22f), tint);
        }
    }

    [PunRPC]
    void RPC_Detach()
    {
        Attached = false;
        anchorViewID = -1;

        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null)
            movement.Grappling = false;

        // Letting go needed a sound of its own - reported back after the attach and the whiff
        // both already had one. Same bank and clip as the whiff, but at a distinct pitch and
        // volume from both it and the attach thwip (full pitch) - lower than either, so it reads
        // as a release rather than another catch or another miss.
        GameAudio.PlayPitched(GameAudio.Vine, "swish-13", 0.45f, 0.6f);
    }

    /// <summary>
    /// Where the anchor actually is right now, and whether it's still good.
    ///
    /// A player anchor is resolved through their PhotonView every call rather than a cached
    /// transform, so it tracks a moving, network-interpolated target on its own - and so a
    /// PhotonView.Find that comes back null (they disconnected) or an object that's stopped
    /// being active (mid-respawn) reads as an anchor that's gone, rather than needing its own
    /// disconnect/death callback to keep in sync with.
    /// </summary>
    Vector3 ResolveAnchor(out bool valid, out PlayerController targetPlayer)
    {
        targetPlayer = null;

        if (anchorViewID < 0)
        {
            valid = true;
            return anchorWorldPoint;
        }

        PhotonView targetView = PhotonView.Find(anchorViewID);

        if (targetView == null || !targetView.gameObject.activeInHierarchy)
        {
            valid = false;
            return anchorWorldPoint;
        }

        targetPlayer = targetView.GetComponent<PlayerController>();
        valid = true;

        // Chest height on the target rather than their feet, so the pull aims at the body rather
        // than the floor they're standing on.
        return targetView.transform.position + Vector3.up * 1.3f;
    }

    /// <summary>
    /// Speed-scaled, and shares Momentum melee's formula rather than its own number - the brief
    /// was explicit that this should work like the peel, not like a third damage curve to tune.
    /// One hit per attach: a grapple that could tick damage every frame it stayed in arriveDistance
    /// would turn "get close" into "stand there," which is the opposite of what a fast, single
    /// impact is supposed to feel like.
    /// </summary>
    void LandHit(PlayerController target)
    {
        hitLandedThisAttach = true;

        if (MatchState.Mode == MatchMode.TeamDeathmatch
            && player.View != null && target.View != null
            && PlayerColours.SameTeam(player.View.Owner, target.View.Owner))
        {
            return;
        }

        PlayerMovement movement = GetComponent<PlayerMovement>();
        float speed = movement != null ? movement.HorizontalSpeed : 0f;
        float damage = PlayerMovement.MomentumDamage(contactDamage, speed);

        target.TakeDamage(damage, "Vine", false);

        // The same confirmation every other weapon gives - was missing here despite the comment
        // above claiming to mirror SingleShotGun's hit path. RegisterHit's rising pitch too, so
        // a vine kill on a run of good hits sounds like part of the same streak a gun would.
        int hits = player.RegisterHit();
        GameAudio.PlayPitched(GameAudio.Hit, "hit", GameAudio.HitVolume,
                              1f + Mathf.Min(hits - 1, 9) * 0.055f);

        if (player.Hud != null)
        {
            player.Hud.ShowHit(false);
            player.Hud.ShowDamage(target.transform.position + Vector3.up, damage, false);
        }

        // No Juice.Hit here - the arrival freeze in UpdateOwner already covers this moment, and
        // a kill doesn't need a second, larger one stacked on top for a "small" effect to stay
        // small.
    }

    // ---------------------------------------------------------------- visual

    void UpdateLine()
    {
        if (line == null)
        {
            line = gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.numCapVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.sharedMaterial = SharedMaterial();
            line.startColor = vineColour;
            line.endColor = vineColour;
        }

        if (!Attached)
        {
            if (line.enabled)
                line.enabled = false;

            return;
        }

        line.enabled = true;

        Vector3 from = transform.position + Vector3.up * 1.3f;
        Vector3 to = anchorViewID >= 0
            ? ResolveLineEnd()
            : anchorWorldPoint;

        line.SetPosition(0, from);
        line.SetPosition(1, to);
    }

    /// The line's own endpoint lookup, kept separate from ResolveAnchor because every client
    /// draws the rope - not only the owner - and drawing has no business detaching anything if
    /// the target has already vanished; it should just leave the last known point alone for the
    /// one frame before RPC_Detach lands.
    Vector3 ResolveLineEnd()
    {
        PhotonView targetView = PhotonView.Find(anchorViewID);
        return targetView != null ? targetView.transform.position + Vector3.up * 1.3f : anchorWorldPoint;
    }

    static Material SharedMaterial()
    {
        if (sharedMaterial != null)
            return sharedMaterial;

        // Custom/UnlitVertexColor rather than Sprites/Default - same flat unlit colour, but
        // tagged as opaque geometry so it actually writes into the depth+normals buffer
        // ScreenOutline reads. Sprites/Default is a transparent render type, which Unity's
        // depth prepass skips, so the vine never showed up in the toon outline at all.
        Shader shader = Shader.Find("Custom/UnlitVertexColor")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Legacy Shaders/Diffuse")
                        ?? Shader.Find("Standard");

        sharedMaterial = new Material(shader) { name = "~vine" };

        Texture2D dot = new Texture2D(1, 1);
        dot.SetPixel(0, 0, Color.white);
        dot.Apply();

        sharedMaterial.mainTexture = dot;

        return sharedMaterial;
    }
}
