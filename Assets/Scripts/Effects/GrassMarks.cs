using UnityEngine;

/// <summary>
/// Marks in the grass: Red Hot Chili Pepper's burn, a darkened scuff where a shot hit grassy
/// ground, and blood under someone who's been shot, with gore on.
///
/// The jungle floor is mostly grass, and a mark on the ground under it (BulletDecal) is hidden by
/// the blades - so flame on the floor looked like it did nothing, and a bullet into it left nothing
/// at all. Points here go to the grass shader (GrassSurface.shader, GrassMark), which darkens blades
/// near them toward soot, root to tip. Each has its own depth (how dark it gets), hold and fade; a
/// new one on a spot already marked renews it rather than taking another slot.
///
/// Every client works this out from what it saw, so none of it is networked.
/// </summary>
public class GrassMarks : MonoBehaviour
{
    // Keep in step with the arrays in GrassSurface.shader.
    const int Slots = 64;

    /// What a mark turns the blades toward.
    public static readonly Color Soot = new Color(0.05f, 0.035f, 0.02f);
    public static readonly Color Dirt = new Color(0.12f, 0.1f, 0.08f);
    public static readonly Color BloodRed = new Color(0.32f, 0.02f, 0.02f);

    static readonly Vector3[] at = new Vector3[Slots];
    static readonly Color[] colour = new Color[Slots];
    static readonly float[] radius = new float[Slots];
    static readonly float[] depth = new float[Slots];
    static readonly float[] hold = new float[Slots];
    static readonly float[] fade = new float[Slots];
    static readonly float[] placedAt = NewTimes();

    static readonly Vector4[] packedPoints = new Vector4[Slots];
    static readonly Vector4[] packedColours = new Vector4[Slots];
    static readonly float[] packedStrength = new float[Slots];

    static readonly int PointsId = Shader.PropertyToID("_GrassMarkPoints");
    static readonly int ColoursId = Shader.PropertyToID("_GrassMarkColours");
    static readonly int StrengthId = Shader.PropertyToID("_GrassMarkStrength");
    static readonly int CountId = Shader.PropertyToID("_GrassMarkCount");

    static GrassMarks instance;
    static int lastCount = -1;

    /// How many marks the grass is showing - for the probe.
    public static int Live { get; private set; }

    static float[] NewTimes()
    {
        float[] times = new float[Slots];
        for (int i = 0; i < times.Length; i++)
            times[i] = -999f;
        return times;
    }

    static float StrengthOf(int i, float now)
    {
        float age = now - placedAt[i];
        if (age < hold[i])
            return depth[i];
        return depth[i] * Mathf.Clamp01(1f - (age - hold[i]) / Mathf.Max(0.01f, fade[i]));
    }

    /// <summary>
    /// A mark `size` across round `point`, turning the blades toward `tone` - `darkness` 1 all the
    /// way, less for a scuff.
    /// </summary>
    public static void Add(Vector3 point, float size, Color tone, float darkness, float holdSeconds, float fadeSeconds)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("~GrassMarks");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GrassMarks>();
        }

        float now = Time.time;
        int free = -1;
        int weakest = 0;
        float weakestStrength = float.MaxValue;

        for (int i = 0; i < Slots; i++)
        {
            float s = StrengthOf(i, now);

            if (s <= 0f)
            {
                if (free < 0)
                    free = i;
                continue;
            }

            if (colour[i] == tone && (at[i] - point).sqrMagnitude < size * size * 0.25f)
            {
                placedAt[i] = now;
                radius[i] = Mathf.Max(radius[i], size);
                depth[i] = Mathf.Max(depth[i], darkness);
                hold[i] = Mathf.Max(hold[i], holdSeconds);
                fade[i] = Mathf.Max(fade[i], fadeSeconds);
                return;
            }

            if (s < weakestStrength)
            {
                weakestStrength = s;
                weakest = i;
            }
        }

        int slot = free >= 0 ? free : weakest;
        at[slot] = point;
        colour[slot] = tone;
        radius[slot] = size;
        depth[slot] = darkness;
        hold[slot] = holdSeconds;
        fade[slot] = fadeSeconds;
        placedAt[slot] = now;
    }

    void Update()
    {
        float now = Time.time;
        int count = 0;

        // Packed from the start, so the shader only loops over the live ones.
        for (int i = 0; i < Slots; i++)
        {
            float s = StrengthOf(i, now);
            if (s <= 0f)
                continue;

            packedPoints[count] = new Vector4(at[i].x, at[i].y, at[i].z, radius[i]);
            packedColours[count] = colour[i];
            packedStrength[count] = s;
            count++;
        }

        Live = count;

        if (count == 0 && lastCount == 0)
            return;

        // Always the full arrays: a global array's size is fixed by the first set.
        Shader.SetGlobalVectorArray(PointsId, packedPoints);
        Shader.SetGlobalVectorArray(ColoursId, packedColours);
        Shader.SetGlobalFloatArray(StrengthId, packedStrength);
        Shader.SetGlobalFloat(CountId, count);
        lastCount = count;
    }
}
