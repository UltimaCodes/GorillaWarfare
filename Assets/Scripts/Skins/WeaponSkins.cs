using System.Collections.Generic;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// Puts a finish on a built weapon - its own, anyone else's, the lobby gorilla's, the previews'.
///
/// Called by WeaponLoadout for every weapon it builds, with the finish its owner published (see
/// SkinInventory.EquippedBy), and again whenever something wants to show a different one. A null
/// finish is the stock look, put back exactly as it was built.
/// </summary>
public static class WeaponSkins
{
    public static void Dress(SingleShotGun gun, Player owner) =>
        Apply(gun, gun != null ? SkinInventory.EquippedBy(owner, gun.name) : null);

    public static void Apply(SingleShotGun gun, WeaponFinish finish)
    {
        if (gun == null || gun.VisualRoot == null)
            return;

        FinishView view = gun.VisualRoot.GetComponent<FinishView>();
        if (view == null)
        {
            if (finish == null)
                return;

            view = gun.VisualRoot.gameObject.AddComponent<FinishView>();
            view.Capture(gun);
        }

        view.Show(finish);
    }

    /// A finish on something that isn't a weapon - a crate's chest. Every mesh under it is painted.
    public static void ApplyToModel(GameObject model, WeaponFinish finish)
    {
        if (model == null)
            return;

        FinishView view = model.GetComponent<FinishView>();
        if (view == null)
        {
            view = model.AddComponent<FinishView>();
            view.Capture(null, false, model.GetComponentsInChildren<MeshRenderer>(true));
        }

        view.Show(finish);
    }

    // ---------------------------------------------------------------- materials

    static readonly Dictionary<(WeaponFinish, Texture), Material> materials = new Dictionary<(WeaponFinish, Texture), Material>();
    static Shader shader;

    /// One material per finish per weapon texture (the bananas share one, the food-kit weapons
    /// another), made once and shared by every weapon that wears it.
    public static Material MaterialFor(WeaponFinish finish, Texture weaponTexture)
    {
        if (materials.TryGetValue((finish, weaponTexture), out Material made) && made != null)
            return made;

        if (shader == null)
            shader = Shader.Find("Custom/WeaponFinish");
        if (shader == null)
        {
            Debug.LogWarning("[skins] no Custom/WeaponFinish shader - finishes can't be drawn");
            return null;
        }

        made = new Material(shader) { name = $"~finish {finish.key}" };
        Fill(made, finish, weaponTexture);
        materials[(finish, weaponTexture)] = made;
        return made;
    }

    static void Fill(Material m, WeaponFinish f, Texture weaponTexture)
    {
        m.SetTexture("_MainTex", weaponTexture != null ? weaponTexture : Texture2D.whiteTexture);
        m.SetColor("_Paint", f.paint);
        m.SetFloat("_Repaint", f.repaint);
        m.SetFloat("_Metallic", f.metallic);
        m.SetFloat("_Glossiness", f.smoothness);

        m.SetTexture("_PatternTex", f.pattern != null ? f.pattern : Texture2D.blackTexture);
        m.SetColor("_PatternColour", f.pattern != null ? f.patternColour : Color.clear);
        m.SetFloat("_PatternRepeats", f.patternRepeats);
        m.SetVector("_PatternScroll", f.patternScroll);
        m.SetFloat("_PatternGlow", f.patternGlow);

        m.SetColor("_GlowColour", f.glow);
        m.SetFloat("_GlowStrength", f.glowStrength);
        m.SetFloat("_PulseSpeed", f.pulseSpeed);
        m.SetFloat("_PulseDepth", f.pulseDepth);

        m.SetFloat("_HueCycle", f.hueCycle);
        m.SetFloat("_HueSpread", f.hueSpread);

        m.SetColor("_RimColour", f.rim);
        m.SetFloat("_RimStrength", f.rimStrength);
        m.SetFloat("_RimPower", f.rimPower);

        m.SetColor("_SweepColour", f.glint);
        m.SetFloat("_SweepEvery", f.glintEvery);
        m.SetFloat("_SweepWidth", f.glintWidth);

        m.SetFloat("_Wobble", f.wobble);
        m.SetFloat("_WobbleSpeed", f.wobbleSpeed);
    }

    // ---------------------------------------------------------------- aura

    static readonly Dictionary<(Texture, bool), Material> auraMaterials = new Dictionary<(Texture, bool), Material>();

    /// <summary>
    /// The finish's particles, streaming off a box the length of the weapon - built in the space
    /// of `root`, whose +Z is the way the weapon points. Your own weapon keeps them round it
    /// (local space) so they stay with the gun on screen; everyone else's leave them in the air
    /// behind as they move.
    /// </summary>
    public static GameObject BuildAura(Transform root, WeaponFinish f, Bounds span, bool local)
    {
        if (f == null || f.auraSprite == null || f.auraRate <= 0f)
            return null;

        GameObject host = new GameObject("~aura");
        host.layer = root.gameObject.layer;
        host.transform.SetParent(root, false);
        host.transform.localPosition = span.center;

        ParticleSystem system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float length = Mathf.Max(span.size.z, 0.1f);
        float girth = Mathf.Max(Mathf.Max(span.size.x, span.size.y), 0.04f);

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(f.auraLife * 0.7f, f.auraLife * 1.3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.04f);
        // auraSize is for a weapon 0.6m long - a longer one throws bigger particles, so every
        // weapon wears the same aura.
        float unit = length / 0.6f;
        main.startSize = new ParticleSystem.MinMaxCurve(f.auraSize * 0.6f * unit, f.auraSize * 1.4f * unit);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = f.auraRainbow ? new ParticleSystem.MinMaxGradient(Rainbow()) { mode = ParticleSystemGradientMode.RandomColor }
                                        : new ParticleSystem.MinMaxGradient(f.auraColour);
        main.maxParticles = 160;
        main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = f.auraRate;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        float shell = f.auraMotion == WeaponFinish.Motion.Orbit ? 2.2f : 1f;
        shape.scale = new Vector3(girth * shell, girth * shell, length);

        // Fades in, fades out - nothing pops.
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient alpha = new Gradient();
        alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = alpha;

        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, f.auraMotion == WeaponFinish.Motion.Sparkle
            ? new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f))
            : new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f)));

        ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
        spin.enabled = true;
        spin.z = new ParticleSystem.MinMaxCurve(-2f, 2f);

        switch (f.auraMotion)
        {
            case WeaponFinish.Motion.Rise:
                main.gravityModifier = -0.035f;
                break;

            case WeaponFinish.Motion.Fall:
                main.gravityModifier = 0.12f;
                break;

            case WeaponFinish.Motion.Drift:
            {
                ParticleSystem.NoiseModule noise = system.noise;
                noise.enabled = true;
                noise.strength = 0.15f;
                noise.frequency = 1.5f;
                noise.scrollSpeed = 0.4f;
                break;
            }

            case WeaponFinish.Motion.Orbit:
            {
                ParticleSystem.VelocityOverLifetimeModule orbit = system.velocityOverLifetime;
                orbit.enabled = true;
                orbit.space = ParticleSystemSimulationSpace.Local;
                orbit.orbitalZ = new ParticleSystem.MinMaxCurve(3.5f);
                orbit.x = orbit.y = orbit.z = new ParticleSystem.MinMaxCurve(0f);
                break;
            }
        }

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = AuraMaterial(f.auraSprite, f.auraGlows);
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;

        system.Play();
        return host;
    }

    static Material AuraMaterial(Texture sprite, bool glows)
    {
        if (auraMaterials.TryGetValue((sprite, glows), out Material made) && made != null)
            return made;

        Shader particles = glows
            ? Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive")
            : Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Alpha Blended");
        particles = particles != null ? particles : Shader.Find("Sprites/Default");

        made = new Material(particles) { name = $"~aura {sprite.name}", mainTexture = sprite };
        auraMaterials[(sprite, glows)] = made;
        return made;
    }

    /// The colour wheel as a gradient, for a rainbow aura.
    static Gradient Rainbow()
    {
        Gradient g = new Gradient();
        GradientColorKey[] keys = new GradientColorKey[6];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new GradientColorKey(Color.HSVToRGB(i / 6f, 0.8f, 1f), i / 5f);
        g.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }
}

/// <summary>
/// What a weapon's model was built with, kept so a finish can come off again, and whatever the
/// finish on it now added - its aura, or a model of your own drawn instead of the stock one.
/// Lives on the weapon's visual root.
/// </summary>
public class FinishView : MonoBehaviour
{
    Renderer[] renderers;
    Material[][] stock;
    string weapon;
    bool local;
    Bounds span;

    GameObject aura;
    GameObject custom;

    public WeaponFinish Showing { get; private set; }

    static MaterialPropertyBlock block;

    public void Capture(SingleShotGun gun) => Capture(gun.name, gun.IsOwned, gun.VisualRenderers);

    /// Any model at all - the crate screen dresses its chests with finishes of their own.
    public void Capture(string weaponKey, bool isLocal, Renderer[] parts)
    {
        weapon = weaponKey;
        local = isLocal;
        renderers = parts ?? GetComponentsInChildren<MeshRenderer>(true);
        stock = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
            stock[i] = renderers[i] != null ? renderers[i].sharedMaterials : new Material[0];

        span = SpanOf(renderers, transform);
    }

    public void Show(WeaponFinish finish)
    {
        if (renderers == null)
            return;

        Showing = finish;

        if (aura != null)
            Destroy(aura);
        if (custom != null)
            Destroy(custom);
        aura = custom = null;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
                continue;
            renderers[i].enabled = true;
            renderers[i].sharedMaterials = stock[i];
        }

        if (finish == null)
            return;

        GameObject model = finish.ModelFor(weapon);
        if (model != null)
        {
            // Your own model, where the stock one was, in its own materials.
            foreach (Renderer r in renderers)
            {
                if (r != null)
                    r.enabled = false;
            }

            custom = Instantiate(model, transform);
            custom.name = "~customModel";
            foreach (Transform t in custom.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = gameObject.layer;
        }
        else
        {
            if (block == null)
                block = new MaterialPropertyBlock();

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                    continue;

                Material[] painted = new Material[stock[i].Length];
                for (int m = 0; m < painted.Length; m++)
                {
                    Texture texture = stock[i][m] != null && stock[i][m].HasProperty("_MainTex") ? stock[i][m].mainTexture : null;
                    painted[m] = WeaponSkins.MaterialFor(finish, texture) ?? stock[i][m];
                }

                r.sharedMaterials = painted;

                // Which way the weapon runs in this mesh's own space, and where along that its
                // model starts and ends - so every weapon's pattern and rainbow run the same way.
                Vector3 axis = r.transform.InverseTransformDirection(transform.forward).normalized;
                (float start, float length) = Extent(r, axis);
                r.GetPropertyBlock(block);
                block.SetVector("_Axis", axis);
                block.SetVector("_Span", new Vector4(start, length, 0f, 0f));
                r.SetPropertyBlock(block);
            }
        }

        aura = WeaponSkins.BuildAura(transform, finish, span, local);
    }

    static (float start, float length) Extent(Renderer r, Vector3 axis)
    {
        Mesh mesh = r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        if (mesh == null)
            return (0f, 1f);

        Bounds b = mesh.bounds;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            float d = Vector3.Dot(corner, axis);
            lo = Mathf.Min(lo, d);
            hi = Mathf.Max(hi, d);
        }

        return (lo, Mathf.Max(hi - lo, 1e-3f));
    }

    /// The model's bounds in the visual root's own space - the aura's box.
    static Bounds SpanOf(Renderer[] renderers, Transform root)
    {
        Bounds span = new Bounds();
        bool started = false;

        foreach (Renderer r in renderers)
        {
            if (r == null || !r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                continue;

            Bounds b = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 inRoot = root.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!started) { span = new Bounds(inRoot, Vector3.zero); started = true; }
                else span.Encapsulate(inRoot);
            }
        }

        return started ? span : new Bounds(Vector3.forward * 0.2f, new Vector3(0.08f, 0.08f, 0.4f));
    }
}
