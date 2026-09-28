using UnityEngine;

/// <summary>
/// Red Hot Chili Pepper's char on a gorilla - only where the flame touched it, not the whole
/// body. Reported: "charring should not just turn the entire enemy monkey black, it should char
/// the areas where the flame hits". The first version tinted the whole body toward black.
///
/// Each touch is a point on the hitbox the flame reached, kept in that hitbox's own space - a
/// hitbox is a child of the bone it covers, so a burnt forearm stays burnt on the forearm as the
/// arm swings. CharOverlay.shader draws the body once more on top, multiplying soot in round those
/// points and nowhere else. A mark holds for a second and fades back over three (the "go back to
/// normal over 3-5 seconds" that was asked for); flame on a spot already marked renews it.
///
/// Runs on every client that can see the body, fed by every client's own flame puffs
/// (LightProjectile), so nothing about it goes over the network. Not on your own body in first
/// person - you can't see it.
/// </summary>
[DefaultExecutionOrder(1000)]   // after MonkeyRig has posed the bones this frame
public class BodyChar : MonoBehaviour
{
    // Keep in step with CHAR_POINTS in CharOverlay.shader.
    public const int Points = 16;

    const float Hold = 1f;
    const float Fade = 3f;

    struct Mark
    {
        public Transform bone;
        public Vector3 local;
        public float radius;
        public float at;
    }

    readonly Mark[] marks = new Mark[Points];
    readonly Vector4[] points = new Vector4[Points];
    readonly float[] strength = new float[Points];

    SkinnedMeshRenderer[] skins;
    Material[][] original;
    int[] overlayIndex;
    MaterialPropertyBlock block;
    bool overlaid;

    static Material overlay;

    /// How many marks are showing - for the probe.
    public int Showing { get; private set; }

    public static BodyChar On(GameObject body)
    {
        BodyChar burn = body.GetComponent<BodyChar>();
        if (burn == null)
            burn = body.AddComponent<BodyChar>();
        return burn;
    }

    /// <summary>
    /// Chars round `point` on `part` - the hitbox the flame reached. `point` should be on its
    /// surface; the radius is how far the soot spreads.
    /// </summary>
    public void Touch(Transform part, Vector3 point, float radius)
    {
        if (part == null)
            return;

        float now = Time.time;
        int free = -1;
        int oldest = 0;

        for (int i = 0; i < Points; i++)
        {
            Mark mark = marks[i];

            if (mark.bone == null || now - mark.at > Hold + Fade)
            {
                if (free < 0)
                    free = i;
                continue;
            }

            // Already black there - keep it black rather than stacking another mark on it.
            if (mark.bone == part
                && (mark.bone.TransformPoint(mark.local) - point).sqrMagnitude < radius * radius * 0.25f)
            {
                marks[i].at = now;
                return;
            }

            if (mark.at < marks[oldest].at)
                oldest = i;
        }

        int slot = free >= 0 ? free : oldest;
        marks[slot] = new Mark { bone = part, local = part.InverseTransformPoint(point), radius = radius, at = now };

        Overlay(true);
    }

    void LateUpdate()
    {
        if (!overlaid)
            return;

        float now = Time.time;
        int showing = 0;

        for (int i = 0; i < Points; i++)
        {
            Mark mark = marks[i];
            float s = 0f;

            if (mark.bone != null)
            {
                float age = now - mark.at;
                s = age < Hold ? 1f : Mathf.Clamp01(1f - (age - Hold) / Fade);
                points[i] = mark.bone.TransformPoint(mark.local);
                points[i].w = mark.radius;
            }

            strength[i] = s;
            if (s > 0f)
                showing++;
        }

        Showing = showing;

        if (showing == 0)
        {
            Overlay(false);
            return;
        }

        if (block == null)
            block = new MaterialPropertyBlock();

        for (int i = 0; i < skins.Length; i++)
        {
            if (skins[i] == null || overlayIndex[i] < 0)
                continue;

            // Its own block, on the overlay's material slot only - the body's tint block (MonkeyRig)
            // is left exactly as it was.
            skins[i].GetPropertyBlock(block, overlayIndex[i]);
            block.SetVectorArray("_CharPoints", points);
            block.SetFloatArray("_CharStrength", strength);
            skins[i].SetPropertyBlock(block, overlayIndex[i]);
        }
    }

    void OnDisable() => Overlay(false);

    /// The extra pass on, or off again - only while something is charred, so a clean body costs
    /// nothing.
    void Overlay(bool on)
    {
        if (on == overlaid)
            return;

        if (on)
        {
            if (overlay == null)
            {
                Shader shader = Shader.Find("Custom/CharOverlay");
                if (shader == null)
                {
                    Debug.LogWarning("[char] no Custom/CharOverlay shader - bodies won't char");
                    return;
                }

                overlay = new Material(shader) { name = "~char" };
            }

            skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            original = new Material[skins.Length][];
            overlayIndex = new int[skins.Length];

            for (int i = 0; i < skins.Length; i++)
            {
                original[i] = skins[i].sharedMaterials;
                overlayIndex[i] = -1;

                // A body you're only ever shown the shadow of - your own, in first person.
                if (skins[i].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly || !skins[i].enabled)
                    continue;

                Material[] with = new Material[original[i].Length + 1];
                original[i].CopyTo(with, 0);
                with[with.Length - 1] = overlay;
                skins[i].sharedMaterials = with;
                overlayIndex[i] = with.Length - 1;
            }
        }
        else if (skins != null)
        {
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i] != null && original[i] != null)
                    skins[i].sharedMaterials = original[i];
            }

            for (int i = 0; i < Points; i++)
                marks[i] = default;

            Showing = 0;
        }

        overlaid = on;
    }
}
