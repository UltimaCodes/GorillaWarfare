using UnityEngine;
using Photon.Pun;

/// <summary>
/// A thing that travels, lands somewhere, and pushes everything nearby.
///
/// Every weapon in the game so far is a raycast that arrives the instant you click. This is the
/// first that takes time to get there, which is the whole reason the pineapple launcher is worth
/// building - a shot you can see coming is a shot you can move out of, and one that pushes you
/// is a shot you can ride.
///
/// Who does what, because it matters and is easy to get wrong:
///
/// - **Everyone** simulates the flight. It is deterministic - a start, a direction and a speed -
///   so nothing needs replicating beyond the shot itself, and everybody watches the same
///   pineapple arc across the same room.
/// - **Only the shooter** deals damage. That matches how every other weapon here works: hit
///   registration is client-authoritative, which is trivially cheatable and completely fine
///   among five friends.
/// - **Everyone applies knockback to themselves.** This is the important one. Your own launch
///   has to happen on your own machine with no round trip, or rocket jumping feels like it is
///   happening to somebody else - and since each client owns its own movement, it is also the
///   only client whose answer counts.
/// </summary>
public class Projectile : MonoBehaviour
{
    GunInfo info;
    PlayerController shooter;
    bool mine;

    Vector3 velocity;
    float bornAt;
    float travelled;

    /// How far it has to get before it will detonate on anything.
    ///
    /// Without this, firing while standing against a wall blows you up instead of launching you,
    /// and the weapon's best trick - shooting the floor at your feet to get somewhere - becomes
    /// the way you die.
    float arming;

    const float MaxLife = 6f;
    const float Radius = 0.12f;

    public static Projectile Launch(GunInfo from, Vector3 origin, Vector3 direction,
                                    PlayerController owner, bool ownedByMe)
    {
        GameObject host = new GameObject($"~{from.name}Shell");
        host.transform.position = origin;
        host.transform.rotation = Quaternion.LookRotation(direction);

        Projectile shell = host.AddComponent<Projectile>();
        shell.info = from;
        shell.shooter = owner;
        shell.mine = ownedByMe;
        shell.velocity = direction.normalized * Mathf.Max(1f, from.projectileSpeed);
        shell.bornAt = Time.time;
        shell.arming = from.armingDistance;

        shell.BuildVisual();

        return shell;
    }

    /// The pineapple itself, borrowed from the same model the weapon uses.
    void BuildVisual()
    {
        GameObject model = Resources.Load<GameObject>($"Models/Weapons/{info.name}");

        if (model == null)
            return;

        GameObject visual = Instantiate(model, transform);
        visual.transform.localPosition = Vector3.zero;

        // Small, and spinning. A pineapple at weapon scale is a dinner plate in the air;
        // tumbling is what makes it read as thrown rather than as floating.
        visual.transform.localScale = Vector3.one * 0.6f;

        foreach (Collider stray in visual.GetComponentsInChildren<Collider>(true))
            Destroy(stray);

        // The material has to be applied by hand. The weapon does it when it builds its own
        // model and the projectile never did, so the thrown fruit arrived untextured - the FBX
        // carries a material slot but not the material, and an unassigned slot renders white.
        Material skin = Resources.Load<Material>($"Models/Weapons/{info.name}Mat")
                        ?? Resources.Load<Material>($"Models/Weapons/Banana{info.name}Mat");

        if (skin == null)
        {
            Debug.LogWarning($"[projectile] no material for {info.name} - it will render untextured");
            return;
        }

        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>(true))
            r.sharedMaterial = skin;
    }

    void Update()
    {
        float step = Time.deltaTime;

        // Gravity, so it arcs. A launcher that fires flat is a slow sniper rifle; the arc is
        // what makes it a different weapon and what lets you drop one behind cover.
        velocity += Physics.gravity * info.projectileGravity * step;

        Vector3 move = velocity * step;
        float distance = move.magnitude;

        // Swept rather than teleported. At forty metres a second a projectile moves most of a
        // metre per frame, and a point test at each end goes straight through people.
        if (Physics.SphereCast(transform.position, Radius, move.normalized, out RaycastHit hit,
                               distance, Hitbox.WorldMask | (1 << LayerMask.NameToLayer(Hitbox.LayerName)),
                               QueryTriggerInteraction.Ignore))
        {
            bool armed = travelled + hit.distance >= arming;
            bool ownBody = shooter != null && hit.collider.transform.IsChildOf(shooter.transform);

            if (armed || !ownBody)
            {
                transform.position = hit.point - move.normalized * Radius;
                Explode();
                return;
            }
        }

        transform.position += move;
        transform.rotation = Quaternion.LookRotation(velocity.normalized);
        transform.Rotate(Vector3.right, Time.time * 720f, Space.Self);

        travelled += distance;

        // Anything still in the air after this went out of the map or through a hole in it.
        if (Time.time - bornAt > MaxLife)
            Explode();
    }

    void Explode()
    {
        Vector3 at = transform.position;
        float radius = Mathf.Max(0.5f, info.blastRadius);

        Effects(at, radius);

        Collider[] caught = Physics.OverlapSphere(at, radius,
            1 << LayerMask.NameToLayer(Hitbox.LayerName), QueryTriggerInteraction.Ignore);

        // One entry per player, not per hitbox. A blast that catches somebody's head, chest and
        // both legs would otherwise hit them four times and delete them from across the room.
        System.Collections.Generic.HashSet<PlayerController> hitPlayers =
            new System.Collections.Generic.HashSet<PlayerController>();

        foreach (Collider collider in caught)
        {
            PlayerController player = collider.GetComponentInParent<PlayerController>();

            if (player != null)
                hitPlayers.Add(player);
        }

        foreach (PlayerController player in hitPlayers)
        {
            Vector3 toward = player.transform.position + Vector3.up - at;
            float distance = toward.magnitude;

            // Linear falloff. Anything fancier is unreadable to the person being hit.
            float strength = Mathf.Clamp01(1f - distance / radius);

            bool self = player == shooter;

            // Knockback is applied by whoever owns that body, and only to their own, because
            // movement is owned locally and a client cannot push somebody else's character.
            if (player.View != null && player.View.IsMine)
            {
                Vector3 push = (toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.up);

                // Biased upward. A blast that only shoves you sideways slides you along the
                // floor; the lift is what turns it into a jump.
                push = (push + Vector3.up * 0.6f).normalized;

                float force = (self ? info.selfKnockback : info.knockback) * strength;
                player.Launch(push * force);
            }

            if (!mine)
                continue;

            // Damage from the shooter's client only, same as every other weapon here.
            float damage = info.damage * strength * (self ? info.selfDamageScale : 1f);

            if (damage > 0.5f)
                player.TakeDamage(damage, info.name, false);
        }
    }

    static Sprite[] boomShapes;

    /// <summary>
    /// The bang, in layers.
    ///
    /// A single sprite and a single sample is a firework. What reads as an explosion is several
    /// things arriving in a deliberate order: a white core that is gone almost immediately, a
    /// fireball that outlives it, sparks thrown outward, and smoke that hangs around after
    /// everything else has finished. Nothing here is clever - it is just more than one thing.
    /// </summary>
    void Effects(Vector3 at, float radius)
    {
        // The crack, then the body a beat later. The delay is the point: sound arriving all at
        // once is a sample, and sound arriving in two parts is an event with a size.
        GameAudio.PlayAt(GameAudio.Explosion, at, GameAudio.ExplosionVolume, 0.06f);
        GameAudio.PlayAtDelayed(GameAudio.Explosion, at, GameAudio.ExplosionBodyVolume, 0.55f, 0.05f);

        if (boomShapes == null || boomShapes.Length == 0)
            boomShapes = Resources.LoadAll<Sprite>("Particles/Boom");

        if (boomShapes.Length > 0)
        {
            Sprite core = Pick("circle");
            Sprite fire = Pick("fire");
            Sprite smoke = Pick("smoke");
            Sprite spark = Pick("spark");

            // Core: small, white hot, over in three frames.
            FlashSprite.Spawn(core, at, radius * 0.5f, radius * 1.6f, 0.09f,
                              new Color(1f, 0.98f, 0.85f, 1f));

            // Fireball: the shape you actually read as the explosion.
            FlashSprite.Spawn(fire, at, radius * 0.7f, radius * 2.3f, 0.32f,
                              new Color(1f, 0.62f, 0.18f, 1f));

            // Smoke, drifting up and outliving the rest. Placed slightly high so it reads as
            // rising out of the blast rather than sitting in it.
            FlashSprite.Spawn(smoke, at + Vector3.up * radius * 0.35f,
                              radius * 0.9f, radius * 2.8f, 0.85f,
                              new Color(0.35f, 0.32f, 0.30f, 0.75f));

            // Sparks thrown outward. Random directions rather than a ring, because a ring reads
            // as a shockwave decal and this should read as debris.
            for (int i = 0; i < 6; i++)
            {
                Vector3 away = Random.onUnitSphere;
                away.y = Mathf.Abs(away.y) * 0.6f + 0.15f;

                FlashSprite.Spawn(spark, at + away * radius * 0.45f,
                                  radius * 0.22f, radius * 0.05f, 0.28f,
                                  new Color(1f, 0.85f, 0.4f, 1f));
            }
        }

        // Scorch on whatever it went off against, so the explosion leaves the world changed
        // rather than only the screen.
        if (Physics.Raycast(at + Vector3.up * 0.4f, Vector3.down, out RaycastHit ground,
                            radius, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            BulletDecal.Spawn(ground.point, ground.normal, Hitbox.WorldMask);

        // Shake scaled by how close it went off, so somebody else's pineapple across the map is
        // a thump and your own at your feet is an event.
        Camera camera = PlayerController.LocalCamera;

        if (camera != null)
        {
            float distance = Vector3.Distance(camera.transform.position, at);
            Juice.Shake(Mathf.Clamp01(1f - distance / (radius * 3f)));
        }

        Destroy(gameObject);
    }

    /// Picks a sprite whose name starts with a prefix, so the pack can gain variants without
    /// this needing to know their numbers.
    static Sprite Pick(string prefix)
    {
        Sprite fallback = null;

        for (int i = 0; i < boomShapes.Length; i++)
        {
            if (fallback == null)
                fallback = boomShapes[i];

            if (boomShapes[i].name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                return boomShapes[i];
        }

        return fallback;
    }
}
