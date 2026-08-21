using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Appends the outline pass (Custom/ToonOutline) to a set of renderers as an extra material
/// slot, rather than replacing whatever shader they already use - the gorillas still do their
/// own team tint through a MaterialPropertyBlock (see MonkeyRig.Tint) and the projectiles still
/// use their own weapon material, both completely untouched by this.
///
/// Materials are cached and shared per (colour, width) pair rather than built fresh per call -
/// the same reason Tint uses a property block instead of touching renderer.material: assigning
/// through renderer.materials (plural) instantiates copies, one per renderer, which is exactly
/// the batching and memory cost this project has already gone out of its way to avoid elsewhere.
/// renderer.sharedMaterials with a cached, shared Material reference avoids that entirely.
/// </summary>
public static class ToonOutline
{
    static readonly Dictionary<(Color, float), Material> cache = new Dictionary<(Color, float), Material>();

    public static void ApplyTo(Renderer[] renderers, Color color, float width)
    {
        if (renderers == null || renderers.Length == 0)
            return;

        Material outline = Get(color, width);

        if (outline == null)
            return;

        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            Material[] existing = r.sharedMaterials;

            // Already applied - respawns and re-equips call this again, and doubling the
            // outline material up would draw the shell pass twice for nothing.
            if (existing.Length > 0 && existing[0] == outline)
                continue;

            Material[] withOutline = new Material[existing.Length + 1];

            // First slot, not last - the outline pass has to draw before the real material so
            // the real geometry's depth write can cover it everywhere except the silhouette
            // edge. Drawn after, the two would fight at the boundary instead.
            withOutline[0] = outline;
            existing.CopyTo(withOutline, 1);

            r.sharedMaterials = withOutline;
        }
    }

    static Material Get(Color color, float width)
    {
        (Color, float) key = (color, width);

        if (cache.TryGetValue(key, out Material cached) && cached != null)
            return cached;

        Shader shader = Shader.Find("Custom/ToonOutline");

        if (shader == null)
        {
            Debug.LogWarning("[outline] Custom/ToonOutline shader not found");
            return null;
        }

        Material mat = new Material(shader) { name = $"~Outline_{color}_{width}" };
        mat.SetColor("_OutlineColor", color);
        mat.SetFloat("_OutlineWidth", width);

        cache[key] = mat;
        return mat;
    }
}
