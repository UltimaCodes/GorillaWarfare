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
    const float LiftOff = 0.008f;

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
    /// on a light surface - reported as the impact "disappearing". This is the part that was
    /// actually missing: a small, fast burst right at the moment of the hit, the same shapes and
    /// spawn pattern the grenade's own explosion already uses, just far smaller and quicker.
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
        FlashSprite.Spawn(core, point + normal * 0.02f, 0.10f, 0.22f, 0.07f, tint);

        // A handful of sparks kicked off the surface, biased along the normal so they read as
        // debris leaving the impact rather than a ring painted on it.
        int count = bloody ? 4 : 6;

        for (int i = 0; i < count; i++)
        {
            Vector3 away = (normal * 0.6f + Random.insideUnitSphere * 0.5f).normalized;
            FlashSprite.Spawn(spark, point + away * 0.05f, 0.05f, 0.01f,
                              Random.Range(0.12f, 0.2f), tint);
        }
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
