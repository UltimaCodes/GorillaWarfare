using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A small, cheap thing in flight: one of Purple Haze's grapes, or a puff of Red Hot Chili
/// Pepper's flame.
///
/// The Grenada's Projectile builds a light, a trail and a glow for every shell - fine for one
/// pineapple at a time, far too heavy for fourteen grapes a second from every gatling in the room.
/// These are pooled, drawn with one shared mesh or quad, and do nothing but move and sweep.
///
/// Same split as Projectile: every client flies them, so everyone sees the same grapes; only the
/// shooter's copies carry a `resolver` - the gun that fired them - and only those deal damage,
/// through the same SingleShotGun.ResolveHit a trace uses.
///
/// - A **pellet** flies with a little gravity, and stops on the first thing it touches.
/// - A **flame** puff travels out, grows and fades; it passes through people, burning each one
///   once, and stops at walls.
/// </summary>
public class LightProjectile : MonoBehaviour
{
    enum Kind { Pellet, Flame }

    static readonly Stack<LightProjectile> pool = new Stack<LightProjectile>();

    /// How many are in the air right now. The probe reads it: more than one grape at once is the
    /// difference between physical projectiles and a trace.
    public static int Live { get; private set; }

    static Mesh grapeMesh;
    static Material grapeMaterial;
    static Sprite[] flameSprites;

    static int sweepMask = -1;
    static int hitboxMask = -1;

    const float PelletRadius = 0.06f;
    const float PelletLife = 3f;
    // A grape about this wide, whatever size the bunch was modelled at.
    const float GrapeWidth = 0.09f;
    static float grapeScale = 1f;

    Kind kind;
    GunInfo info;
    PlayerController shooter;
    SingleShotGun resolver;
    Vector3 velocity;
    float bornAt;
    float travelled;
    readonly HashSet<Object> burned = new HashSet<Object>();

    MeshFilter filter;
    MeshRenderer view;
    MaterialPropertyBlock block;
    float spin;

    /// A grape. `resolver` is the gun that fired it, on the shooter's own client only.
    public static void FirePellet(GunInfo from, Vector3 origin, Vector3 direction, PlayerController by,
                                  SingleShotGun resolver)
    {
        LightProjectile shot = Take();
        shot.Begin(Kind.Pellet, from, origin, direction.normalized * Mathf.Max(1f, from.projectileSpeed), by, resolver);
    }

    /// A puff of flame, carrying some of the shooter's own speed so running forward while firing
    /// doesn't overtake your own fire.
    public static void FireFlame(GunInfo from, Vector3 origin, Vector3 direction, Vector3 inherited,
                                 PlayerController by, SingleShotGun resolver)
    {
        LightProjectile puff = Take();
        puff.Begin(Kind.Flame, from, origin, direction.normalized * from.flameSpeed + inherited * 0.6f, by, resolver);
    }

    static LightProjectile Take()
    {
        while (pool.Count > 0)
        {
            LightProjectile pooled = pool.Pop();
            if (pooled != null)
            {
                pooled.gameObject.SetActive(true);
                return pooled;
            }
        }

        GameObject host = new GameObject("~light");
        Object.DontDestroyOnLoad(host);
        LightProjectile made = host.AddComponent<LightProjectile>();
        made.filter = host.AddComponent<MeshFilter>();
        made.view = host.AddComponent<MeshRenderer>();
        made.view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        made.view.receiveShadows = false;
        made.view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        made.block = new MaterialPropertyBlock();
        return made;
    }

    void Begin(Kind what, GunInfo from, Vector3 origin, Vector3 startVelocity, PlayerController by, SingleShotGun gun)
    {
        kind = what;
        info = from;
        shooter = by;
        resolver = gun;
        velocity = startVelocity;
        bornAt = Time.time;
        travelled = 0f;
        burned.Clear();
        transform.position = origin;
        spin = Random.Range(0f, 360f);
        Live++;

        if (kind == Kind.Pellet)
        {
            filter.sharedMesh = GrapeMesh();
            view.sharedMaterial = grapeMaterial;
            view.SetPropertyBlock(null);
            transform.localScale = Vector3.one * grapeScale;
            transform.rotation = Random.rotation;
        }
        else
        {
            Sprite sprite = FlameSprite();
            filter.sharedMesh = FlashSprite.Quad();
            view.sharedMaterial = FlashSprite.Additive();
            if (sprite != null)
            {
                view.GetPropertyBlock(block);
                block.SetTexture("_MainTex", sprite.texture);
                view.SetPropertyBlock(block);
            }
        }
    }

    void Update()
    {
        float step = Time.deltaTime;
        float age = Time.time - bornAt;

        if (kind == Kind.Pellet)
            FlyPellet(step, age);
        else
            FlyFlame(step, age);
    }

    // ---------------------------------------------------------------- grapes

    void FlyPellet(float step, float age)
    {
        velocity += Physics.gravity * info.projectileGravity * step;
        Vector3 move = velocity * step;
        float distance = move.magnitude;

        if (distance > 0f && Physics.SphereCast(transform.position, PelletRadius, move / distance, out RaycastHit hit,
                                                distance, SweepMask, QueryTriggerInteraction.Ignore)
            && !OwnBody(hit.collider))
        {
            travelled += hit.distance;

            // Only the shooter's grape decides what it did; everyone sees it burst.
            if (resolver != null)
                resolver.ResolveHit(hit.collider, hit.point, travelled, info.DamageAtRange(travelled));

            Splat(hit.point);
            Return();
            return;
        }

        transform.position += move;
        travelled += distance;

        if (age > PelletLife)
            Return();
    }

    void Splat(Vector3 at)
    {
        Sprite shape = FlameSprite(smoke: true);
        FlashSprite.Spawn(shape, at, 0.08f, 0.3f, 0.18f, new Color(0.55f, 0.2f, 0.75f, 0.9f));
    }

    // ---------------------------------------------------------------- flame

    void FlyFlame(float step, float age)
    {
        float life = Mathf.Max(0.05f, info.flameLife);
        float t = age / life;

        if (t >= 1f)
        {
            Return();
            return;
        }

        float radius = Mathf.Lerp(info.flameRadiusStart, info.flameRadiusEnd, t);

        // Fire slows as it spreads, rather than holding its muzzle speed out to the tip.
        velocity *= Mathf.Max(0f, 1f - 1.6f * step);
        velocity += Vector3.up * 1.5f * step;

        Vector3 move = velocity * step;
        float distance = move.magnitude;

        // Walls stop it - world only, people are burned below and passed through.
        if (distance > 0f && Physics.SphereCast(transform.position, radius * 0.5f, move / distance, out RaycastHit wall,
                                                distance, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
        {
            transform.position = wall.point + wall.normal * radius * 0.5f;
            velocity = Vector3.ProjectOnPlane(velocity, wall.normal) * 0.3f;
        }
        else
        {
            transform.position += move;
        }

        if (resolver != null)
            BurnWhatItTouches(radius, t);

        Draw(radius, t);
    }

    /// Each person (or dummy) once per puff, harder close to the nozzle.
    void BurnWhatItTouches(float radius, float t)
    {
        Collider[] touched = Physics.OverlapSphere(transform.position, radius, HitboxMask | Hitbox.WorldMask,
                                                   QueryTriggerInteraction.Ignore);

        foreach (Collider collider in touched)
        {
            if (OwnBody(collider))
                continue;

            // Explicit checks, not ?? - GetComponentInParent can hand back Unity's fake null in the
            // editor, which ?? doesn't see as null.
            Object target = null;
            PlayerController player = collider.GetComponentInParent<PlayerController>();
            if (player != null)
                target = player;
            else
            {
                TrainingDummy dummy = collider.GetComponentInParent<TrainingDummy>();
                if (dummy != null)
                    target = dummy;
            }

            if (target == null || burned.Contains(target))
                continue;

            burned.Add(target);

            float damage = info.damage * Mathf.Lerp(1f, info.flameTipDamage, t);
            resolver.ResolveFlameHit(collider, transform.position, damage);
        }
    }

    void Draw(float radius, float t)
    {
        Camera camera = PlayerController.LocalCamera != null ? PlayerController.LocalCamera : Camera.main;
        if (camera != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, camera.transform.up)
                                 * Quaternion.Euler(0f, 0f, spin + t * 90f);
        }

        // Drawn well past the radius it burns in: the sprites are thin licks of flame with a lot of
        // clear space round them, and at the burn radius the stream read as a pencil line.
        transform.localScale = Vector3.one * radius * 3.2f;

        // Yellow-white at the nozzle, orange, then a dark red as it dies - and fading out.
        Color hot = Color.Lerp(new Color(1f, 0.85f, 0.45f), new Color(1f, 0.35f, 0.05f), Mathf.Clamp01(t * 1.6f));
        Color colour = Color.Lerp(hot, new Color(0.5f, 0.08f, 0.02f), Mathf.Clamp01((t - 0.6f) / 0.4f));
        float alpha = 1f - t * t;

        view.GetPropertyBlock(block);
        block.SetColor("_TintColor", new Color(colour.r, colour.g, colour.b, 0.8f * alpha));
        block.SetColor("_Color", new Color(colour.r, colour.g, colour.b, 0.8f * alpha));
        view.SetPropertyBlock(block);
    }

    // ---------------------------------------------------------------- plumbing

    bool OwnBody(Collider collider) => shooter != null && collider.transform.IsChildOf(shooter.transform);

    void Return()
    {
        Live = Mathf.Max(0, Live - 1);
        resolver = null;
        shooter = null;
        gameObject.SetActive(false);
        pool.Push(this);
    }

    static int SweepMask
    {
        get
        {
            if (sweepMask < 0)
                sweepMask = Hitbox.WorldMask | HitboxMask;
            return sweepMask;
        }
    }

    static int HitboxMask
    {
        get
        {
            if (hitboxMask < 0)
                hitboxMask = 1 << LayerMask.NameToLayer(Hitbox.LayerName);
            return hitboxMask;
        }
    }

    /// One of the grape bunch's own meshes, shrunk - so a grape in flight is the weapon's grapes,
    /// not a shape made up for it.
    static Mesh GrapeMesh()
    {
        if (grapeMesh != null)
            return grapeMesh;

        GameObject model = Resources.Load<GameObject>("Models/Weapons/Gatling");
        MeshFilter source = model != null ? model.GetComponentInChildren<MeshFilter>() : null;
        grapeMesh = source != null ? source.sharedMesh : null;
        grapeMaterial = Resources.Load<Material>("Models/Weapons/GatlingMat");

        if (grapeMesh != null)
        {
            Vector3 size = grapeMesh.bounds.size;
            grapeScale = GrapeWidth / Mathf.Max(0.001f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        }

        if (grapeMesh == null || grapeMaterial == null)
            Debug.LogWarning("[weapons] no grape model or material in Resources/Models/Weapons - grapes will be invisible");

        return grapeMesh;
    }

    static Sprite FlameSprite(bool smoke = false)
    {
        if (flameSprites == null || flameSprites.Length == 0)
            flameSprites = Resources.LoadAll<Sprite>("Particles/Boom");

        if (flameSprites.Length == 0)
            return null;

        List<Sprite> wanted = new List<Sprite>();
        foreach (Sprite s in flameSprites)
        {
            if (s.name.StartsWith(smoke ? "smoke" : "fire"))
                wanted.Add(s);
        }

        return wanted.Count > 0 ? wanted[Random.Range(0, wanted.Count)] : flameSprites[0];
    }
}
