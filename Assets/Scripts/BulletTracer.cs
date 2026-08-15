using UnityEngine;

/// <summary>
/// The streak a shot leaves through the air.
///
/// Hitscan weapons have a readability problem: the shot arrives the instant you click, so unless
/// it hits something with a visible reaction there is nothing at all to tell you it happened.
/// You end up firing into the middle distance and guessing. A tracer is the cheapest fix - it
/// draws the line the bullet took, so a miss reads as a miss that went *there* rather than as
/// nothing at all.
///
/// Drawn from the muzzle rather than from the camera. Those are different points, and using the
/// camera is the classic mistake: shots appear to come out of your forehead, and at close range
/// the line is edge-on and invisible exactly when you most want the feedback.
/// </summary>
public class BulletTracer : MonoBehaviour
{
    const float LifeSeconds = 0.055f;
    const float StartWidth = 0.035f;
    const float EndWidth = 0.008f;

    static Material shared;

    LineRenderer line;
    float bornAt;

    /// <param name="colour">
    /// Tinted per weapon so a shotgun blast and a sniper shot don't read as the same event.
    /// </param>
    public static void Spawn(Vector3 from, Vector3 to, Color colour)
    {
        // Nothing to draw, and a zero length line renderer warns rather than doing nothing.
        if ((to - from).sqrMagnitude < 0.0004f)
            return;

        GameObject host = new GameObject("~tracer");
        BulletTracer tracer = host.AddComponent<BulletTracer>();
        tracer.Build(from, to, colour);
    }

    void Build(Vector3 from, Vector3 to, Color colour)
    {
        bornAt = Time.time;

        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);

        // Wide at the muzzle, thin at the far end. Reads as motion rather than as a wire.
        line.startWidth = StartWidth;
        line.endWidth = EndWidth;

        line.numCapVertices = 0;
        line.alignment = LineAlignment.View;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        line.sharedMaterial = SharedMaterial();
        line.startColor = colour;
        line.endColor = new Color(colour.r, colour.g, colour.b, 0f);
    }

    void Update()
    {
        float age = (Time.time - bornAt) / LifeSeconds;

        if (age >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        // Fade and thin together, so it reads as the streak passing rather than as a line being
        // switched off.
        float left = 1f - age;

        Color start = line.startColor;
        start.a = left;
        line.startColor = start;

        line.startWidth = StartWidth * left;
        line.endWidth = EndWidth * left;
    }

    // Additive, so a tracer brightens whatever it crosses instead of painting a grey stripe over
    // it, and so overlapping shots stack into something worth looking at.
    static Material SharedMaterial()
    {
        if (shared != null)
            return shared;

        Shader shader = Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        shared = new Material(shader);

        // A one pixel white texture, so the material's colour is all that decides how it looks.
        Texture2D dot = new Texture2D(1, 1);
        dot.SetPixel(0, 0, Color.white);
        dot.Apply();

        shared.mainTexture = dot;
        shared.renderQueue = 3000;

        return shared;
    }
}
