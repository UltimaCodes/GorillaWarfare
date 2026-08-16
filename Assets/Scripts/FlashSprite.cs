using UnityEngine;

/// <summary>
/// A billboarded sprite that flares and dies. The muzzle flash, the explosion and the impact
/// puff are all this with different numbers.
///
/// Billboarded every frame toward the camera rather than oriented once when it spawns. A quad
/// that only faces the right way at birth goes edge-on and vanishes the moment you strafe -
/// which is why the muzzle flash was a point light in the first place, and why it read as a
/// lamp being switched on rather than as a weapon firing.
///
/// Unlit and additive, so it never picks up scene lighting and never darkens: a flash that
/// respects shadows looks like a decal.
/// </summary>
public class FlashSprite : MonoBehaviour
{
    static Material additive;
    static Mesh quad;

    MeshRenderer view;
    MaterialPropertyBlock block;

    float born;
    float life;
    float startScale;
    float endScale;
    Color tint;
    float spin;

    /// <summary>
    /// Throws one up and lets it clean itself up.
    ///
    /// Parented when a parent is given, so a muzzle flash rides the weapon while the gun is
    /// still moving. Unparented for world effects like explosions, which should stay where they
    /// happened rather than follow whoever caused them.
    /// </summary>
    public static FlashSprite Spawn(Sprite sprite, Vector3 at, float size, float endSize,
                                    float seconds, Color colour, Transform parent = null)
    {
        if (sprite == null)
            return null;

        GameObject host = new GameObject("~flash");
        host.transform.position = at;

        if (parent != null)
            host.transform.SetParent(parent, true);

        FlashSprite flash = host.AddComponent<FlashSprite>();
        flash.Begin(sprite, size, endSize, seconds, colour);

        return flash;
    }

    void Begin(Sprite sprite, float size, float endSize, float seconds, Color colour)
    {
        born = Time.unscaledTime;
        life = Mathf.Max(0.01f, seconds);
        startScale = size;
        endScale = endSize;
        tint = colour;

        // A different roll each time. Four muzzle sprites all landing at the same angle reads as
        // a repeating animation; a random twist makes each shot look like its own event.
        spin = Random.Range(0f, 360f);

        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = Quad();

        view = gameObject.AddComponent<MeshRenderer>();
        view.sharedMaterial = Additive();
        view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        view.receiveShadows = false;
        view.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;

        block = new MaterialPropertyBlock();
        block.SetTexture("_MainTex", sprite.texture);
        view.SetPropertyBlock(block);

        Draw(0f);
    }

    void LateUpdate()
    {
        // Unscaled, because a muzzle flash during hitstop should still be over in a frame or
        // two rather than hanging on the screen through the freeze.
        float age = (Time.unscaledTime - born) / life;

        if (age >= 1f)
        {
            Destroy(gameObject);
            return;
        }

        Draw(age);
    }

    void Draw(float age)
    {
        Camera camera = PlayerController.LocalCamera;

        if (camera != null)
        {
            // Face the camera, then roll about the view axis. Rolling in the sprite's own space
            // would tilt it out of the billboard.
            transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position,
                                                        camera.transform.up)
                                 * Quaternion.Euler(0f, 0f, spin);
        }

        transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, age);

        // Squared fade. Holds its brightness for most of its life then leaves quickly, which is
        // what a real flash does - a linear fade reads as a dimmer being turned down.
        float alpha = 1f - age * age;

        if (view != null && block != null)
        {
            view.GetPropertyBlock(block);
            block.SetColor("_TintColor", new Color(tint.r, tint.g, tint.b, tint.a * alpha));
            block.SetColor("_Color", new Color(tint.r, tint.g, tint.b, tint.a * alpha));
            view.SetPropertyBlock(block);
        }
    }

    /// <summary>
    /// One material for every flash in the game, so hundreds of them still batch.
    ///
    /// Particles/Additive is a built-in shader, which matters: it means there is no shader asset
    /// to include in the build and nothing to go missing.
    /// </summary>
    static Material Additive()
    {
        if (additive != null)
            return additive;

        Shader shader = Shader.Find("Particles/Additive")
                        ?? Shader.Find("Legacy Shaders/Particles/Additive")
                        ?? Shader.Find("Sprites/Default");

        additive = new Material(shader) { name = "~flash", enableInstancing = true };

        return additive;
    }

    static Mesh Quad()
    {
        if (quad != null)
            return quad;

        quad = new Mesh { name = "~flashQuad" };

        quad.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
        };

        quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        quad.RecalculateBounds();

        return quad;
    }
}
