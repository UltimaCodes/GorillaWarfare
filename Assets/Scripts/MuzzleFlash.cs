using UnityEngine;

// The flash at the muzzle when the gun fires.
//
// This used to be a point light at intensity 6 with a 7 metre range and nothing else. Fired at
// open ground it read as a shot; fired at a wall two metres away it read as somebody switching a
// lamp on and off in your face, because that is exactly what it was. A muzzle flash is a shape,
// not an illumination - the light is the small part.
//
// So it is a real ParticleSystem now, billboarded at the barrel tip, with a light that is a fifth
// as bright and less than half the range purely to catch the wall behind it.
//
// Rebuilt 2026-08-22 from a single hand-placed FlashSprite billboard, reported as looking bad
// and specifically as not using Unity's actual particle system - both true. One quad is one
// shape at one size; a burst is several, each with its own size, speed and spin, which is what a
// real muzzle flash actually looks like - overlapping cones of debris, not a single sticker. Uses
// `Emit()` rather than `Play()` on every shot, deliberately - `Play()` restarts the system and
// clears whatever's still alive, and the rifle is automatic. A shot landing mid-burst from the
// last one needs to add to it, not cut it off.
public class MuzzleFlash : MonoBehaviour
{
    [Tooltip("How bright the accompanying light is. Deliberately small - the sprite is the "
             + "flash, this is only what it throws onto nearby surfaces.")]
    [SerializeField] float intensity = 1.2f;

    [Tooltip("Metres. Short enough that firing at a wall lights the wall rather than the room.")]
    [SerializeField] float range = 2.6f;

    [SerializeField] float decay = 30f;

    [Tooltip("Metres across. Scaled up by longer weapons, which have bigger muzzles.")]
    // Raised from 0.42 alongside the switch to the star/circle sprites below - those are a
    // tighter, more contained shape than the old soft photographic blob, and read as small next
    // to the tracers (0.07m wide now, up from 0.035m the same day) at the old size.
    [SerializeField] float flashSize = 0.55f;

    [SerializeField] float flashSeconds = 0.06f;

    [Tooltip("Particles per shot.")]
    [SerializeField] int burstCount = 7;

    static Sprite[] shapes;
    static Material additive;

    Light flash;
    ParticleSystem particles;
    ParticleSystemRenderer particlesView;
    float level;
    float tipDistance = 0.35f;

    /// The point shots leave from. Tracers start here rather than at the camera - those are
    /// different places, and drawing from the camera makes shots appear to come out of your
    /// forehead and go edge-on invisible at exactly the range you want the feedback.
    public Transform Tip => flash != null ? flash.transform : transform;

    /// <summary>
    /// Hides or shows the burst renderer while scoped, the same way `SingleShotGun.SetVisible`
    /// already hides the weapon model itself. Added 2026-08-22 - the old FlashSprite version was
    /// a fresh, self-destroying object per shot and was essentially never alive at the moment of
    /// an aim toggle, so nothing had ever needed to hide it on purpose. This one is a persistent
    /// `ParticleSystemRenderer` that exists for the weapon's whole lifetime, whether or not it's
    /// currently emitting - which is exactly the kind of renderer `SetVisible` exists to catch,
    /// it just didn't know this one existed yet.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (particlesView != null)
            particlesView.enabled = visible;
    }

    /// Where the business end is, in the weapon's local space.
    public void SetTipDistance(float distance)
    {
        tipDistance = distance;

        if (flash != null)
            flash.transform.localPosition = new Vector3(0f, 0f, tipDistance);
    }

    void Awake()
    {
        Build();
    }

    // Separate from Awake so it can be exercised outside play mode, where Awake never runs on
    // AddComponent and the light would silently never exist.
    public void Build()
    {
        if (flash != null)
            return;

        GameObject host = new GameObject("~MuzzleFlash");
        host.transform.SetParent(transform, false);

        // Out at the barrel tip. The banana models are built along +Z, so this sits at the end
        // of one rather than inside the grip. The distance is set by the weapon once it knows
        // how long its model is - a fixed value put the pistol's flash out in open air and the
        // sniper's somewhere in the middle of the fruit.
        host.transform.localPosition = new Vector3(0f, 0f, tipDistance);

        flash = host.AddComponent<Light>();
        flash.type = LightType.Point;
        flash.color = new Color(1f, 0.85f, 0.5f);
        flash.range = range;
        flash.intensity = 0f;
        flash.enabled = false;

        BuildParticles(host.transform);
    }

    void BuildParticles(Transform tip)
    {
        GameObject host = new GameObject("~MuzzleBurst");
        host.transform.SetParent(tip, false);

        particles = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.gravityModifier = 0f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(flashSize * 0.4f, flashSize * 0.95f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = new Color(1f, 0.86f, 0.55f, 1f);
        main.maxParticles = 64;

        // No emission on its own - Fire() calls Emit() directly, so a shot always adds to
        // whatever's already alive instead of the system deciding when to burst.
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        // A narrow forward cone rather than a sphere - gas leaving a barrel travels roughly one
        // way, not in every direction at once.
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = 0.01f;

        // Squared fade, same shape every other flash in this game fades on - holds near full
        // brightness for most of its short life, then leaves quickly rather than dimming evenly.
        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.4f, 0.3f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        // Grows slightly then holds, rather than only shrinking - a flash that only fades looks
        // like a light going out, one that expands looks like gas actually leaving the barrel.
        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = particles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.4f, 1.15f), new Keyframe(1f, 1f)));

        particlesView = host.GetComponent<ParticleSystemRenderer>();
        particlesView.renderMode = ParticleSystemRenderMode.Billboard;
        particlesView.sharedMaterial = SharedAdditive();
        particlesView.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particlesView.receiveShadows = false;
        particlesView.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
    }

    /// <summary>
    /// The muzzle sprites.
    ///
    /// Was its own set in Resources/Particles/Muzzle - soft, photographic gunpowder-flash
    /// textures, picked before this game had settled on a visual language. Reported as looking
    /// "ugly and out of place" once the toon outline shipped, and looking at the actual sprite
    /// confirms why: it's a soft realistic falloff sitting right next to a bold graphic black
    /// line, while everything else that flashes - the bullet impact puff, the vine's latch spark,
    /// the grenade's own explosion - already draws from Particles/Boom's star and circle shapes,
    /// which read as flat and graphic rather than photographic. Switched to the same bank rather
    /// than sourcing a new one, so every spark in the game is now visually one family instead of
    /// the muzzle being the one exception.
    /// </summary>
    static Sprite[] Shapes()
    {
        if (shapes != null && shapes.Length > 0)
            return shapes;

        Sprite[] all = Resources.LoadAll<Sprite>("Particles/Boom");
        System.Collections.Generic.List<Sprite> picked = new System.Collections.Generic.List<Sprite>();

        foreach (Sprite s in all)
        {
            if (s.name.StartsWith("star", System.StringComparison.OrdinalIgnoreCase)
                || s.name.StartsWith("circle", System.StringComparison.OrdinalIgnoreCase))
            {
                picked.Add(s);
            }
        }

        shapes = picked.Count > 0 ? picked.ToArray() : all;

        if (shapes.Length == 0)
            Debug.LogWarning("[muzzle] no sprites in Resources/Particles/Boom - falling back to the light alone");

        return shapes;
    }

    public void Fire()
    {
        level = 1f;

        if (flash != null)
            flash.enabled = true;

        if (particles == null || particlesView == null)
            return;

        Sprite[] set = Shapes();

        if (set.Length > 0)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            particlesView.GetPropertyBlock(block);
            block.SetTexture("_MainTex", set[Random.Range(0, set.Length)].texture);
            particlesView.SetPropertyBlock(block);
        }

        // A longer weapon's tip sits further from the grip, which reads as a bigger gun and
        // wants a bigger flash - the same scaling the old single-sprite version used.
        float scale = Mathf.Max(0.6f, tipDistance / 0.35f);
        ParticleSystem.MainModule main = particles.main;
        main.startSize = new ParticleSystem.MinMaxCurve(flashSize * 0.4f * scale, flashSize * 0.95f * scale);

        // Emit rather than Play - adds to whatever's already alive instead of restarting the
        // system and clearing it, which matters the moment an automatic weapon fires again before
        // the last burst has finished dying.
        particles.Emit(burstCount);
    }

    static Material SharedAdditive()
    {
        if (additive != null)
            return additive;

        Shader shader = Shader.Find("Particles/Additive")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        additive = new Material(shader) { name = "~muzzleBurst", enableInstancing = true };
        return additive;
    }

    void Update()
    {
        if (flash == null || level <= 0f)
            return;

        level -= decay * Time.deltaTime;

        if (level <= 0f)
        {
            level = 0f;
            flash.enabled = false;   // disabled rather than zero intensity, so it costs nothing
            return;
        }

        flash.intensity = intensity * level;
    }
}
