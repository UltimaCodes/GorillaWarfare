using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Being on fire - Red Hot Chili Pepper's afterburn, TF2's rule that the flame keeps hurting
/// after it stops touching you.
///
/// On every player and on any training dummy that gets lit. On a player's own client it ticks
/// the damage (through PlayerController.TakeBurn, so the kill goes to whoever lit you); on
/// everyone else's it only shows the flames, from a flag the owner streams. On a dummy - local,
/// like everything in the one-person sandbox - it does both.
///
/// What it looks like has to be obvious at a glance across a fight - the first version was a
/// few static sprites near the body and was reported as "VERY subtle, i cant tell that they
/// are". Now a burning body has flames rising off it the whole time, smoke above them and an
/// orange light flickering on everything round it. The person burning doesn't see their own
/// body in first person, so for them the flames lick up from the bottom of the view.
///
/// The char is not here - it goes only where the flame actually touched (BodyChar), not over
/// the whole body.
/// </summary>
public class Afterburn : MonoBehaviour
{
    const float TickSeconds = 0.25f;

    const float FireRate = 55f;
    const float SmokeRate = 14f;
    const float OwnViewFireRate = 60f;

    float burningUntil;
    float perSecond;
    Player igniter;
    float nextTick;
    bool shownRemotely;

    PlayerController player;
    TrainingDummy dummy;
    MonkeyRig rig;

    ParticleSystem fire;
    ParticleSystem smoke;
    Light glow;
    float flickerSeed;

    static Material additive;
    static Material alphaBlended;
    static Texture fireTexture;
    static Texture smokeTexture;

    /// Whether this body is burning, as far as this client decides it.
    public bool Burning => Time.time < burningUntil;

    /// Whether this client is showing it burning - its own decision on the owner's client or a
    /// dummy, the owner's streamed flag on everybody else's.
    public bool ShowingFire { get; private set; }

    /// The flames and the light, for the probe to see they're actually going.
    public ParticleSystem Fire => fire;
    public Light Glow => glow;

    public static Afterburn On(GameObject target)
    {
        Afterburn burn = target.GetComponent<Afterburn>();
        if (burn == null)
            burn = target.AddComponent<Afterburn>();
        return burn;
    }

    void Awake()
    {
        player = GetComponent<PlayerController>();
        dummy = GetComponent<TrainingDummy>();
        rig = GetComponent<MonkeyRig>();
        if (rig == null)
            rig = GetComponentInChildren<MonkeyRig>();

        flickerSeed = Random.Range(0f, 100f);
    }

    /// Lit, or lit again - more flame refreshes the burn rather than stacking it.
    public void Ignite(float seconds, float damagePerSecond, Player by)
    {
        if (!Burning)
            nextTick = Time.time + TickSeconds;

        burningUntil = Mathf.Max(burningUntil, Time.time + seconds);
        perSecond = Mathf.Max(damagePerSecond, Burning ? perSecond : 0f);
        igniter = by;
    }

    /// A remote copy's flames, from the owner's stream.
    public void ShowBurning(bool burning) => shownRemotely = burning;

    bool Mine => player == null || (player.View != null && player.View.IsMine);

    // The owner's own body isn't drawn for them - fire on it would be flames round the inside of
    // their own head.
    bool FirstPerson => player != null && Mine;

    void Update()
    {
        bool mine = Mine;
        ShowingFire = mine ? Burning : shownRemotely;

        Show(ShowingFire);

        if (!mine || !Burning || Time.time < nextTick)
            return;

        nextTick = Time.time + TickSeconds;
        float damage = perSecond * TickSeconds;

        if (player != null)
            player.TakeBurn(damage, igniter);
        else if (dummy != null)
            dummy.TakeDamage(damage, "Flamer", false);
    }

    void Show(bool burning)
    {
        if (burning && fire == null)
            Build();

        if (fire != null)
        {
            ParticleSystem.EmissionModule emission = fire.emission;
            emission.rateOverTime = burning ? (FirstPerson ? OwnViewFireRate : FireRate) : 0f;
        }

        if (smoke != null)
        {
            ParticleSystem.EmissionModule emission = smoke.emission;
            emission.rateOverTime = burning ? SmokeRate : 0f;
        }

        if (glow != null)
        {
            // Flickers the way firelight does - two noise rates layered, never steady.
            float flicker = Mathf.PerlinNoise(Time.time * 9f, flickerSeed) * 0.6f
                            + Mathf.PerlinNoise(Time.time * 23f, flickerSeed + 7f) * 0.4f;
            float wanted = burning ? Mathf.Lerp(1.4f, 3.2f, flicker) : 0f;
            glow.intensity = Mathf.MoveTowards(glow.intensity, wanted, Time.deltaTime * 12f);
            glow.enabled = glow.intensity > 0.01f;
        }
    }

    void Build()
    {
        EnsureShared();

        if (FirstPerson)
        {
            // Licks of flame up from the bottom of your own view, in the camera's space so they
            // stay there as you look round.
            // Placed from the camera's own field of view (it's a setting, 60 to 120) just inside the
            // bottom edge and the full width across - at a fixed spot they spawned below the frame
            // at the narrow end and only rose into view as they faded out.
            Camera eye = PlayerController.LocalCamera;
            if (eye != null)
            {
                const float Ahead = 0.6f;
                float halfHeight = Mathf.Tan(eye.fieldOfView * 0.5f * Mathf.Deg2Rad) * Ahead;
                float halfWidth = halfHeight * Mathf.Max(1f, eye.aspect);

                fire = BuildFire(eye.transform, new Vector3(0f, -halfHeight * 0.92f, Ahead),
                                 new Vector3(halfWidth * 2f, 0.04f, 0.1f), ParticleSystemSimulationSpace.Local,
                                 0.22f, 0.5f, 0.35f, 0.6f, 0.5f, 1.2f, 1.6f);
            }
        }
        else
        {
            Bounds body = BodyBounds();
            Vector3 centre = transform.InverseTransformPoint(body.center);
            Vector3 size = Vector3.Scale(body.size, new Vector3(0.55f, 0.7f, 0.55f));

            fire = BuildFire(transform, centre, size, ParticleSystemSimulationSpace.World,
                             0.35f, 0.85f, 0.35f, 0.7f, 1.4f, 3f);
            smoke = BuildSmoke(transform, centre + Vector3.up * body.extents.y * 0.6f,
                               new Vector3(size.x, 0.2f, size.z));
        }

        GameObject lightHost = new GameObject("~burnLight");
        lightHost.transform.SetParent(transform, false);
        lightHost.transform.localPosition = Vector3.up * 0.4f;
        glow = lightHost.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.52f, 0.16f);
        glow.range = 5.5f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
        glow.enabled = false;
    }

    /// Where the body is, from its own renderers - the transform's origin sits at a different
    /// height on a player and a dummy.
    Bounds BodyBounds()
    {
        Renderer[] parts = rig != null ? rig.GetComponentsInChildren<SkinnedMeshRenderer>(true) : null;
        if (parts == null || parts.Length == 0)
            return new Bounds(transform.position + Vector3.up * 0.2f, new Vector3(0.7f, 1.6f, 0.7f));

        Bounds bounds = parts[0].bounds;
        for (int i = 1; i < parts.Length; i++)
            bounds.Encapsulate(parts[i].bounds);

        return bounds;
    }

    static ParticleSystem BuildFire(Transform parent, Vector3 at, Vector3 box, ParticleSystemSimulationSpace space,
                                    float minSize, float maxSize, float minLife, float maxLife,
                                    float minRise, float maxRise, float brightness = 1f)
    {
        GameObject host = new GameObject("~fire");
        host.transform.SetParent(parent, false);
        host.transform.localPosition = at;

        ParticleSystem ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = space;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = Color.white;
        main.maxParticles = 160;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        // Up, whichever way the body is facing - flames rise.
        ParticleSystem.VelocityOverLifetimeModule rise = ps.velocityOverLifetime;
        rise.enabled = true;
        rise.space = space;
        rise.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        rise.y = new ParticleSystem.MinMaxCurve(minRise, maxRise);
        rise.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        // White-hot at the base, orange, red, gone.
        ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
        colour.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.92f, 0.6f), 0f),
                new GradientColorKey(new Color(1f, 0.55f, 0.12f), 0.35f),
                new GradientColorKey(new Color(0.85f, 0.18f, 0.04f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.12f),
                new GradientAlphaKey(0.6f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        colour.color = fade;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.35f)));

        ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

        Finish(ps, additive, fireTexture, brightness);
        return ps;
    }

    static ParticleSystem BuildSmoke(Transform parent, Vector3 at, Vector3 box)
    {
        GameObject host = new GameObject("~smoke");
        host.transform.SetParent(parent, false);
        host.transform.localPosition = at;

        ParticleSystem ps = host.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = new Color(0.16f, 0.14f, 0.13f, 1f);
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = box;

        ParticleSystem.VelocityOverLifetimeModule rise = ps.velocityOverLifetime;
        rise.enabled = true;
        rise.space = ParticleSystemSimulationSpace.World;
        rise.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        rise.y = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        rise.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

        ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
        colour.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));

        Finish(ps, alphaBlended, smokeTexture);
        return ps;
    }

    // `brightness` over the additive shader's default - the licks in your own view sit over bright
    // sky and grass at the edge of the screen, and at 1 they barely registered.
    static void Finish(ParticleSystem ps, Material material, Texture texture, float brightness = 1f)
    {
        ParticleSystemRenderer view = ps.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = material;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        if (texture != null)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetTexture("_MainTex", texture);
            if (!Mathf.Approximately(brightness, 1f))
                block.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f) * brightness);
            view.SetPropertyBlock(block);
        }

        ps.Play();
    }

    static void EnsureShared()
    {
        if (additive == null)
            additive = FlashSprite.Additive();

        if (alphaBlended == null)
        {
            Shader shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended")
                            ?? Shader.Find("Particles/Alpha Blended")
                            ?? Shader.Find("Sprites/Default");
            alphaBlended = new Material(shader) { name = "~smoke", enableInstancing = true };
        }

        if (fireTexture == null || smokeTexture == null)
        {
            foreach (Sprite sprite in Resources.LoadAll<Sprite>("Particles/Boom"))
            {
                if (fireTexture == null && sprite.name.StartsWith("fire"))
                    fireTexture = sprite.texture;
                else if (smokeTexture == null && sprite.name.StartsWith("smoke"))
                    smokeTexture = sprite.texture;
            }
        }
    }
}
