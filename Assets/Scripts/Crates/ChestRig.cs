using System.Collections;
using UnityEngine;

/// <summary>
/// A crate's chest on the crate screen's stage, alive: it bobs and sways while it waits, peeks its
/// lid open when you hover its card, rattles when it's about to go, and bursts open.
///
/// The chests are Kenney's (Assets/Art/Crates, CC0) with the lid a separate part and an "open"
/// clip of their own. The clip is only read, for where the lid ends up; the lid is then driven
/// here, so it can peek a little way, rattle, and fly open past its stop and settle back.
/// </summary>
public class ChestRig : MonoBehaviour
{
    Transform lid;
    Quaternion shut = Quaternion.identity;
    Quaternion open = Quaternion.identity;
    Vector3 home;
    Quaternion homeRotation;
    Vector3 restScale;

    Renderer glow;
    Color colour;
    float glowNow;

    float lidNow;
    float lidTarget;
    bool lidDriven;

    float bob = 1f;
    float shake;
    float hop;
    float scaleTarget = 1f;
    float scaleNow = 1f;
    float seed;

    public bool Hovered { get; private set; }
    public float GlowTarget { get; set; } = 0.45f;

    public static ChestRig On(GameObject chest, Color colour, Texture glowSprite)
    {
        ChestRig rig = chest.AddComponent<ChestRig>();
        rig.colour = colour;
        rig.seed = Random.value * 10f;
        rig.Capture();
        rig.glow = StageFx.Glow(chest.transform.parent, chest.transform.localPosition + new Vector3(0f, 0.35f, 0.45f), glowSprite, colour, 1.9f);
        return rig;
    }

    void Capture()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "lid")
                lid = t;
        }

        if (lid != null)
        {
            shut = lid.localRotation;

            // Where the kit's own open animation leaves the lid.
            Animation clip = GetComponentInChildren<Animation>();
            if (clip != null && clip["open"] != null)
            {
                AnimationState state = clip["open"];
                state.enabled = true;
                state.weight = 1f;
                state.time = state.length;
                clip.Sample();
                open = lid.localRotation;
                state.time = 0f;
                clip.Sample();
                state.enabled = false;
                clip.Stop();
                clip.enabled = false;
                lid.localRotation = shut;
            }
            else
            {
                open = shut * Quaternion.Euler(-110f, 0f, 0f);
            }
        }

        home = transform.localPosition;
        homeRotation = transform.localRotation;
        restScale = transform.localScale;
    }

    /// Where it stands - the crate screen moves it about and re-homes it.
    public void SetHome(Vector3 localPosition) => home = localPosition;
    public Vector3 Home => home;

    public void SetHover(bool on)
    {
        Hovered = on;
        scaleTarget = on ? 1.1f : 1f;
        if (!lidDriven)
            lidTarget = on ? 0.14f : 0f;
        bob = on ? 1.6f : 1f;
    }

    /// One of the three jolts before it bursts - `strength` 0 to 1.
    public void Rattle(float strength)
    {
        shake = Mathf.Max(shake, 0.4f + strength * 0.9f);
        hop = Mathf.Max(hop, 0.03f + strength * 0.07f);
        lidTarget = 0.06f + strength * 0.18f;
        GlowTarget = 0.6f + strength * 0.9f;
    }

    /// The lid flies open past where it stops and settles back.
    public IEnumerator Burst()
    {
        lidDriven = true;
        shake = 0f;
        GlowTarget = 2.2f;
        yield return UiMotion.Tween(0.45f, UiMotion.OutBack, k => lidNow = Mathf.LerpUnclamped(lidNow, 1f, k));
        lidNow = 1f;
    }

    /// Back to shut and still - for the next opening.
    public void Reset()
    {
        lidDriven = false;
        lidNow = lidTarget = 0f;
        shake = hop = 0f;
        GlowTarget = 0.45f;
        if (lid != null)
            lid.localRotation = shut;
    }

    public void Show(bool shown)
    {
        gameObject.SetActive(shown);
        if (glow != null)
            glow.gameObject.SetActive(shown);
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float time = Time.unscaledTime + seed;

        if (!lidDriven)
            lidNow = Mathf.Lerp(lidNow, lidTarget, 1f - Mathf.Exp(-10f * dt));
        if (lid != null)
            lid.localRotation = Quaternion.SlerpUnclamped(shut, open, lidNow);

        shake = Mathf.MoveTowards(shake, 0f, dt * 2.2f);
        hop = Mathf.MoveTowards(hop, 0f, dt * 0.35f);

        float jolt = shake * Mathf.Sin(time * 55f) * 9f;
        float sway = Mathf.Sin(time * 0.7f) * 7f * bob;
        transform.localRotation = homeRotation * Quaternion.Euler(0f, sway, jolt);

        float lift = Mathf.Sin(time * 1.7f) * 0.018f * bob + hop * Mathf.Abs(Mathf.Sin(time * 30f));
        transform.localPosition = home + Vector3.up * lift;

        scaleNow = Mathf.Lerp(scaleNow, scaleTarget, 1f - Mathf.Exp(-12f * dt));
        transform.localScale = restScale * scaleNow;

        glowNow = Mathf.Lerp(glowNow, GlowTarget * (1f + 0.12f * Mathf.Sin(time * 2.3f)), 1f - Mathf.Exp(-6f * dt));
        if (glow != null)
        {
            // The legacy particle shaders tint with _TintColor, and additive scales it by its alpha.
            glow.material.SetColor("_TintColor", new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(glowNow * 0.5f)));
            glow.transform.localScale = Vector3.one * (1.6f + glowNow * 0.6f);
            glow.transform.localPosition = home + new Vector3(0f, 0.35f, 0.45f);
        }
    }

    void OnDestroy()
    {
        if (glow != null)
            Destroy(glow.gameObject);
    }
}

/// <summary>
/// Effects on a preview stage - one-shot particle bursts, showers of coins, soft glows. All from
/// Kenney's sprites and models; all on the stage's layer, so only the stage's camera sees them.
/// </summary>
public static class StageFx
{
    /// A soft glow behind something - a sprite on a quad, adding light.
    public static Renderer Glow(Transform parent, Vector3 localPosition, Texture sprite, Color colour, float size)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "~glow";
        Object.Destroy(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPosition;
        quad.transform.localScale = Vector3.one * size;
        quad.layer = parent.gameObject.layer;

        Renderer view = quad.GetComponent<Renderer>();
        view.material = new Material(Additive()) { mainTexture = sprite };
        view.material.SetColor("_TintColor", colour);
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        return view;
    }

    /// <summary>
    /// A one-shot burst of sprites flying out from a point, cleaned up when it's done. `lift` is
    /// how much they float (negative) or fall (positive).
    /// </summary>
    public static ParticleSystem Burst(Transform parent, Vector3 localPosition, Texture sprite, Color colour, int count,
                                       float speed, float size, float life, bool glows = true, float gravity = 0.4f,
                                       bool rainbow = false, float spread = 360f)
    {
        GameObject host = new GameObject("~burst");
        host.layer = parent.gameObject.layer;
        host.transform.SetParent(parent, false);
        host.transform.localPosition = localPosition;

        ParticleSystem system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.duration = 0.2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life * 1.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = rainbow ? new ParticleSystem.MinMaxGradient(Spectrum()) { mode = ParticleSystemGradientMode.RandomColor }
                                  : new ParticleSystem.MinMaxGradient(colour, Color.Lerp(colour, Color.white, 0.5f));
        main.gravityModifier = gravity;
        main.maxParticles = count + 10;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        ParticleSystem.ShapeModule shape = system.shape;
        if (spread >= 360f)
        {
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spread * 0.5f;
            shape.radius = 0.08f;
            host.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // up
        }

        Fade(system);

        ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.sharedMaterial = new Material(glows ? Additive() : Blended()) { mainTexture = sprite };
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        system.Play();
        return system;
    }

    /// <summary>
    /// Real coins (the platformer kit's), spinning up out of something and falling back - a
    /// shower of `count`, in the coins' own material.
    /// </summary>
    public static ParticleSystem Coins(Transform parent, Vector3 localPosition, GameObject coinModel, int count, float speed, float size)
    {
        MeshFilter filter = coinModel != null ? coinModel.GetComponentInChildren<MeshFilter>() : null;
        MeshRenderer model = coinModel != null ? coinModel.GetComponentInChildren<MeshRenderer>() : null;
        if (filter == null || model == null)
            return null;

        GameObject host = new GameObject("~coins");
        host.layer = parent.gameObject.layer;
        host.transform.SetParent(parent, false);
        host.transform.localPosition = localPosition;
        host.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.duration = 0.3f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.9f;
        main.maxParticles = count + 5;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count), new ParticleSystem.Burst(0.15f, (short)(count / 2)) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 28f;
        shape.radius = 0.1f;

        ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
        spin.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Mesh;
        view.mesh = filter.sharedMesh;
        view.sharedMaterial = model.sharedMaterial;
        view.alignment = ParticleSystemRenderSpace.Local;

        system.Play();
        return system;
    }

    static void Fade(ParticleSystem system)
    {
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient alpha = new Gradient();
        alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
        fade.color = alpha;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0.2f)));
    }

    static Gradient Spectrum()
    {
        Gradient g = new Gradient();
        GradientColorKey[] keys = new GradientColorKey[6];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new GradientColorKey(Color.HSVToRGB(i / 6f, 0.75f, 1f), i / 5f);
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    static Shader additive;
    static Shader blended;

    static Shader Additive()
    {
        if (additive == null)
            additive = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive") ?? Shader.Find("Sprites/Default");
        return additive;
    }

    static Shader Blended()
    {
        if (blended == null)
            blended = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended") ?? Shader.Find("Sprites/Default");
        return blended;
    }
}
