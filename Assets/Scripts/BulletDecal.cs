using UnityEngine;

/// <summary>
/// The mark a shot leaves behind.
///
/// The old one was a 0.02 scale quad from a prefab, dropped at whatever `OverlapSphere` happened
/// to return first - which is how you end up with a small white square hanging in mid air near
/// where you shot rather than a mark on the thing you hit.
///
/// Three things make this read as damage instead of geometry:
///
/// - It multiplies rather than draws over. A multiply blend darkens whatever is underneath, so
///   the mark is automatically a darker version of that surface - concrete, sand, gorilla -
///   without anyone sampling a texture or picking a colour per material. Which is also what a
///   real scorch or a bruise does to the thing it's on.
/// - The texture is a soft blotch with a bit of noise, generated once and shared, so the edge
///   isn't a straight line and no two hits look identical.
/// - It has to land on something. The shooter's client decides where the shot went, but every
///   client re-checks locally that there is still a surface there before drawing anything -
///   which is what stops blood hanging in the air after the body it belonged to has gone.
/// </summary>
public class BulletDecal : MonoBehaviour
{
    // How far to look for a surface either side of the reported hit point. Generous enough to
    // survive a little disagreement between clients, tight enough that a shot into thin air
    // finds nothing.
    const float SearchDistance = 0.35f;

    // Lifted off the surface, or it z-fights with it.
    //
    // Raised from 0.008 on 2026-08-22, investigated after a report that impacts "don't work" on
    // mesh colliders specifically. Built a diagnostic (Tools > Gorilla Warfare, since removed)
    // that re-raycast every mesh and box collider actually in Game.unity the way Spawn() does
    // below - all 67 mesh colliders and all 120 box colliders re-raycast clean, and a decal spawned
    // on a scaled, rotated tree came out an undistorted, correctly sized quad, not the sheared or
    // degenerate shape a non-uniform parent scale would produce. No difference between the two
    // collider types was ever found. What is true: the decal itself has always been a faint
    // multiply blend by design (see the class doc above), and a low-poly decoration mesh has much
    // coarser per-triangle normals than a flat wall - a raycast hitting near a facet seam on a
    // rock or trunk can report a normal that's a few degrees off the true local surface, which at
    // this offset was close enough to risk sitting just inside the mesh instead of just outside
    // it. Raised as a hedge against that specific failure mode rather than left at a value tuned
    // only ever tested against flat, unscaled geometry - if impacts on mesh props still read as
    // missing after this, it needs a person shooting one and saying so, not another guess.
    const float LiftOff = 0.02f;

    const float WorldLifetime = 18f;
    const float BloodLifetime = 7f;
    const float FadeSeconds = 1.2f;

    static readonly Color Scorch = new Color(0.32f, 0.30f, 0.28f, 1f);
    static readonly Color Blood = new Color(0.62f, 0.05f, 0.06f, 1f);

    static Texture2D splat;
    static Material worldMaterial;
    static Material bloodMaterial;
    static Mesh quad;
    static Sprite[] boomShapes;

    Transform anchor;
    Renderer view;
    MaterialPropertyBlock block;
    Color tint;
    float diesAt;

    /// <summary>
    /// Places a mark at a reported hit. Returns null when there is nothing there to mark.
    /// </summary>
    public static BulletDecal Spawn(Vector3 point, Vector3 normal, int shooterLayerMask)
    {
        // Look back along the normal for the surface that was actually hit. Doing this per
        // client rather than trusting the shooter's collider means a decal can never outlive
        // the thing it was drawn on - if the body is gone, there is nothing to find and nothing
        // gets drawn.
        Vector3 from = point + normal * SearchDistance;

        if (!Physics.Raycast(from, -normal, out RaycastHit hit, SearchDistance * 2f,
                             shooterLayerMask, QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        bool bloody = hit.collider.GetComponentInParent<IDamageable>() != null;

        // Added 2026-08-22. Runs here rather than off the damage RPC because that RPC only ever
        // reaches the victim (see PlayerController.TakeDamage) - this, like the rest of
        // PlayFireEffects, is broadcast to every client, which is what a shooter's own hit
        // confirmation actually needs: to be seen by whoever's looking at the target, not just
        // felt by the target themselves.
        if (bloody)
        {
            MonkeyRig rig = hit.collider.GetComponentInParent<MonkeyRig>();
            if (rig != null)
                rig.Flash();
        }

        Puff(hit.point, hit.normal, bloody);

        GameObject host = new GameObject(bloody ? "~blood" : "~impact");
        host.transform.position = hit.point + hit.normal * LiftOff;

        // Face out of the surface, then spin at random so repeated hits don't tile.
        host.transform.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up)
                                  * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        float size = bloody ? Random.Range(0.16f, 0.28f) : Random.Range(0.09f, 0.16f);
        host.transform.localScale = new Vector3(size, size, size);

        // Parented to whatever it landed on, so it moves with a body and dies with it.
        host.transform.SetParent(hit.collider.transform, true);

        BulletDecal decal = host.AddComponent<BulletDecal>();
        decal.Build(hit.collider.transform, bloody);

        return decal;
    }

    void Build(Transform surface, bool bloody)
    {
        EnsureShared();

        anchor = surface;
        tint = bloody ? Blood : Scorch;
        diesAt = Time.time + (bloody ? BloodLifetime : WorldLifetime);

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = quad;

        view = gameObject.AddComponent<MeshRenderer>();
        view.sharedMaterial = bloody ? bloodMaterial : worldMaterial;
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        block = new MaterialPropertyBlock();
        Apply(1f);
    }

    void Update()
    {
        // The surface went away - a player died, or something was destroyed under it. Without
        // this the mark stays exactly where it was, floating.
        if (anchor == null)
        {
            Destroy(gameObject);
            return;
        }

        float left = diesAt - Time.time;

        if (left <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        if (left < FadeSeconds)
            Apply(left / FadeSeconds);
    }

    // A multiply decal fades by going white - white multiplied over anything leaves it alone.
    void Apply(float strength)
    {
        if (view == null)
            return;

        view.GetPropertyBlock(block);
        block.SetColor("_TintColor", Color.Lerp(Color.white, tint, strength));
        block.SetColor("_Color", Color.Lerp(Color.white, tint, strength));
        view.SetPropertyBlock(block);
    }

    /// <summary>
    /// The moment of the hit, not the mark it leaves - FlashSprite's own doc comment already
    /// named "the impact puff" as one of its three jobs, alongside the muzzle flash and the
    /// explosion, but nothing ever actually called it for a bullet impact. The decal alone is a
    /// multiply blend the same colour as most of what it lands on, which reads as barely there
    /// on a light surface - reported as the impact "disappearing".
    ///
    /// Rebuilt 2026-08-22, still reported as not appearing after the first pass. Looked at how
    /// this is actually done elsewhere rather than guess again: the standard shape is a bright
    /// static core plus debris that *travels* - start speed a few metres a second, a shape module
    /// so it sprays rather than sitting still, real particles rather than a handful of
    /// independent stickers. The old version's "sparks" were `FlashSprite`s - billboards that
    /// fade in place at a fixed offset, with no velocity at all - which is exactly why this never
    /// read as an impact: nothing in it actually moved. The core flash stays a `FlashSprite`
    /// (a static bright point is correct for that part, per the same reference), but the debris is
    /// a real `ParticleSystem` burst now, with a Cone shape and actual outward speed plus a little
    /// gravity so it arcs and falls like debris rather than floating.
    /// </summary>
    static void Puff(Vector3 point, Vector3 normal, bool bloody)
    {
        if (boomShapes == null || boomShapes.Length == 0)
            boomShapes = Resources.LoadAll<Sprite>("Particles/Boom");

        if (boomShapes.Length == 0)
            return;

        Sprite spark = Pick("spark");
        Sprite core = Pick("circle");

        Color tint = bloody ? new Color(0.75f, 0.1f, 0.12f, 1f) : new Color(1f, 0.92f, 0.7f, 1f);

        // A quick bright core right at the surface, gone almost instantly - the same "arrives at
        // full size and collapses" trick the explosion's own core uses, just a fraction of it.
        // Offset raised alongside BulletDecal's own LiftOff, same reasoning - a coarse mesh's
        // per-triangle normal has more room to be slightly wrong than a flat wall's.
        FlashSprite.Spawn(core, point + normal * 0.05f, 0.10f, 0.22f, 0.07f, tint);

        SpawnDebris(point + normal * 0.05f, normal, tint, spark, bloody ? 6 : 9);
    }

    /// A short-lived, self-destroying ParticleSystem for debris that actually flies rather than
    /// sitting in place - see Puff()'s doc comment for why this replaced a loop of FlashSprites.
    static void SpawnDebris(Vector3 point, Vector3 normal, Color tint, Sprite sprite, int count)
    {
        if (sprite == null)
            return;

        GameObject host = new GameObject("~debris");
        host.transform.position = point;
        host.transform.rotation = Quaternion.LookRotation(normal);

        ParticleSystem ps = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = tint;
        main.gravityModifier = 1.1f;
        main.maxParticles = 24;

        // Cleans itself up the moment the last particle dies - nothing else has to track or
        // destroy this the way FlashSprite tracked its own lifetime, since there's no per-frame
        // behaviour left to run once Play() has fired the one burst.
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        // A forward-biased cone rather than a full sphere, so debris reads as leaving the impact
        // point along the surface normal instead of an even spray in every direction at once.
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.01f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        colorOverLifetime.color = fade;

        ParticleSystemRenderer view = host.GetComponent<ParticleSystemRenderer>();
        view.renderMode = ParticleSystemRenderMode.Billboard;
        view.sharedMaterial = SharedDebrisMaterial();
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        view.SetPropertyBlock(block);

        ps.Play();
    }

    static Material debrisMaterial;

    static Material SharedDebrisMaterial()
    {
        if (debrisMaterial != null)
            return debrisMaterial;

        Shader shader = Shader.Find("Particles/Additive")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        debrisMaterial = new Material(shader) { name = "~debris", enableInstancing = true };
        return debrisMaterial;
    }

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

    static void EnsureShared()
    {
        if (quad == null)
        {
            quad = new Mesh { name = "~decalQuad" };
            quad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
            };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            quad.RecalculateNormals();
        }

        if (splat == null)
            splat = BuildSplat();

        if (worldMaterial == null)
            worldMaterial = BuildMaterial();

        if (bloodMaterial == null)
            bloodMaterial = BuildMaterial();
    }

    // Multiply blending, so the mark darkens whatever it sits on rather than painting a colour
    // over it. Falls back through a couple of shader names because which of these exists depends
    // on what the project has pulled in.
    static Material BuildMaterial()
    {
        Shader shader = Shader.Find("Legacy Shaders/Particles/Multiply")
                        ?? Shader.Find("Particles/Multiply")
                        ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                        ?? Shader.Find("Sprites/Default");

        Material material = new Material(shader) { mainTexture = splat };
        material.renderQueue = 3000;
        return material;
    }

    /// A round blotch with a soft edge and a bit of noise, so it doesn't read as a shape.
    static Texture2D BuildSplat()
    {
        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "~splat" };

        float seed = Random.Range(0f, 100f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f;
                float dy = (y + 0.5f) / size - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) * 2f;

                // Wobble the radius so the outline isn't a circle either.
                float angle = Mathf.Atan2(dy, dx);
                float wobble = Mathf.PerlinNoise(Mathf.Cos(angle) * 2f + seed, Mathf.Sin(angle) * 2f + seed);
                float radius = 0.62f + wobble * 0.3f;

                float density = Mathf.Clamp01(1f - Mathf.SmoothStep(radius * 0.45f, radius, distance));

                // Speckle, so the middle isn't a flat disc.
                density *= 0.65f + Mathf.PerlinNoise(x * 0.22f + seed, y * 0.22f + seed) * 0.35f;

                // White is "leave the surface alone" under a multiply blend, so density drives
                // how far toward the tint each texel goes.
                float value = 1f - density;
                texture.SetPixel(x, y, new Color(value, value, value, density));
            }
        }

        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }
}
