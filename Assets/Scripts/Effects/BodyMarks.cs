using UnityEngine;

/// <summary>
/// Marks on a gorilla, only where they were made: Red Hot Chili Pepper's char where the flame
/// touched it (LightProjectile), blood where a shot landed (BulletDecal).
///
/// Started as the chili's char - "it should char the areas where the flame hits", not tint the whole
/// body - and took over blood when bullet marks turned out not to show: they were quads stuck to the
/// hitbox, a sphere that sits a little in or out of the mesh, so a fresh one on a chest was a dot of
/// red at best.
///
/// Each mark is a point on the hitbox it was made on, kept in that hitbox's own space - a hitbox is a
/// child of the bone it covers, so a mark on a forearm stays on the forearm as the arm swings.
/// BodyMarks.shader draws the body once more on top, multiplying each mark's colour in round its point
/// and nowhere else. A mark holds, then fades; a new one on a spot already marked the same way renews
/// it rather than taking another slot.
///
/// Runs on every client that can see the body, from what that client saw happen, so nothing about it
/// goes over the network. Not on your own body in first person - you can't see it.
/// </summary>
[DefaultExecutionOrder(1000)]   // after MonkeyRig has posed the bones this frame
public class BodyMarks : MonoBehaviour
{
    // Keep in step with MARKS in BodyMarks.shader.
    public const int Points = 16;

    public static readonly Color Soot = new Color(0.09f, 0.075f, 0.065f);
    public static readonly Color Blood = new Color(0.5f, 0.04f, 0.05f);

    struct Mark
    {
        public Transform bone;
        public Vector3 local;
        public float radius;
        public Color colour;
        public float at;
        public float hold;
        public float fade;

        public float Strength(float now)
        {
            if (bone == null)
                return 0f;

            float age = now - at;
            return age < hold ? 1f : Mathf.Clamp01(1f - (age - hold) / Mathf.Max(0.01f, fade));
        }
    }

    readonly Mark[] marks = new Mark[Points];
    readonly Vector4[] points = new Vector4[Points];
    readonly Vector4[] colours = new Vector4[Points];
    readonly float[] strength = new float[Points];

    SkinnedMeshRenderer[] skins;
    Material[][] original;
    int[] overlayIndex;
    MaterialPropertyBlock block;
    bool overlaid;

    static Material overlay;

    /// How many marks are showing - for the probe.
    public int Showing { get; private set; }

    public static BodyMarks On(GameObject body)
    {
        BodyMarks marks = body.GetComponent<BodyMarks>();
        if (marks == null)
            marks = body.AddComponent<BodyMarks>();
        return marks;
    }

    /// <summary>
    /// A mark round `point` on `part` - the hitbox that was hit, with `point` on its surface - in
    /// `colour`, held for `hold` seconds and faded over `fade`.
    /// </summary>
    public void Add(Transform part, Vector3 point, float radius, Color colour, float hold, float fade)
    {
        if (part == null)
            return;

        float now = Time.time;
        int free = -1;
        int weakest = 0;
        float weakestStrength = float.MaxValue;

        for (int i = 0; i < Points; i++)
        {
            float s = marks[i].Strength(now);

            if (s <= 0f)
            {
                if (free < 0)
                    free = i;
                continue;
            }

            // Already marked there, the same way - renew it rather than stacking another on it.
            if (marks[i].bone == part && marks[i].colour == colour
                && (marks[i].bone.TransformPoint(marks[i].local) - point).sqrMagnitude < radius * radius * 0.25f)
            {
                marks[i].at = now;
                marks[i].hold = Mathf.Max(marks[i].hold, hold);
                marks[i].fade = Mathf.Max(marks[i].fade, fade);
                return;
            }

            if (s < weakestStrength)
            {
                weakestStrength = s;
                weakest = i;
            }
        }

        int slot = free >= 0 ? free : weakest;
        marks[slot] = new Mark
        {
            bone = part, local = part.InverseTransformPoint(point), radius = radius,
            colour = colour, at = now, hold = hold, fade = fade,
        };

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
            float s = marks[i].Strength(now);

            if (s > 0f)
            {
                points[i] = marks[i].bone.TransformPoint(marks[i].local);
                points[i].w = marks[i].radius;
                colours[i] = marks[i].colour;
                showing++;
            }

            strength[i] = s;
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
            block.SetVectorArray("_MarkPoints", points);
            block.SetVectorArray("_MarkColours", colours);
            block.SetFloatArray("_MarkStrength", strength);
            skins[i].SetPropertyBlock(block, overlayIndex[i]);
        }
    }

    void OnDisable() => Overlay(false);

    /// The extra pass on, or off again - only while something is showing, so an unmarked body costs
    /// nothing.
    void Overlay(bool on)
    {
        if (on == overlaid)
            return;

        if (on)
        {
            if (overlay == null)
            {
                Shader shader = Shader.Find("Custom/BodyMarks");
                if (shader == null)
                {
                    Debug.LogWarning("[marks] no Custom/BodyMarks shader - bodies won't show marks");
                    return;
                }

                overlay = new Material(shader) { name = "~bodyMarks" };
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
