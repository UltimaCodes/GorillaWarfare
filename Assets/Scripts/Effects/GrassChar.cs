using UnityEngine;

/// <summary>
/// Red Hot Chili Pepper charring the grass it touches.
///
/// The jungle floor is mostly grass, and a scorch mark on the ground under it (BulletDecal.Char)
/// is hidden by the blades - so flame on the floor looked like it did nothing. Burn points here go
/// to the grass shader (GrassSurface.shader, GrassBurn), which turns blades near them to soot. Same
/// life as every other char: a second held, three fading back; flame on a spot already burnt renews
/// it rather than taking another slot.
///
/// Every client works this out from its own flame puffs, so none of it is networked.
/// </summary>
public class GrassChar : MonoBehaviour
{
    // Keep in step with the arrays in GrassSurface.shader.
    const int Slots = 64;

    const float Hold = 1f;
    const float Fade = 3f;

    static readonly Vector3[] at = new Vector3[Slots];
    static readonly float[] radius = new float[Slots];
    static readonly float[] placedAt = NewTimes();

    static readonly Vector4[] packedPoints = new Vector4[Slots];
    static readonly float[] packedStrength = new float[Slots];

    static readonly int PointsId = Shader.PropertyToID("_GrassCharPoints");
    static readonly int StrengthId = Shader.PropertyToID("_GrassCharStrength");
    static readonly int CountId = Shader.PropertyToID("_GrassCharCount");

    static GrassChar instance;
    static int lastCount = -1;

    /// How many burn points the grass is showing - for the probe.
    public static int Live { get; private set; }

    static float[] NewTimes()
    {
        float[] times = new float[Slots];
        for (int i = 0; i < times.Length; i++)
            times[i] = -999f;
        return times;
    }

    public static void Touch(Vector3 point, float size)
    {
        if (instance == null)
        {
            GameObject host = new GameObject("~GrassChar");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GrassChar>();
        }

        float now = Time.time;
        int free = -1;
        int oldest = 0;

        for (int i = 0; i < Slots; i++)
        {
            if (now - placedAt[i] > Hold + Fade)
            {
                if (free < 0)
                    free = i;
                continue;
            }

            if ((at[i] - point).sqrMagnitude < size * size * 0.25f)
            {
                placedAt[i] = now;
                radius[i] = Mathf.Max(radius[i], size);
                return;
            }

            if (placedAt[i] < placedAt[oldest])
                oldest = i;
        }

        int slot = free >= 0 ? free : oldest;
        at[slot] = point;
        radius[slot] = size;
        placedAt[slot] = now;
    }

    void Update()
    {
        float now = Time.time;
        int count = 0;

        // Packed from the start, so the shader only loops over the live ones.
        for (int i = 0; i < Slots; i++)
        {
            float age = now - placedAt[i];
            if (age > Hold + Fade)
                continue;

            packedPoints[count] = new Vector4(at[i].x, at[i].y, at[i].z, radius[i]);
            packedStrength[count] = age < Hold ? 1f : Mathf.Clamp01(1f - (age - Hold) / Fade);
            count++;
        }

        Live = count;

        if (count == 0 && lastCount == 0)
            return;

        // Always the full arrays: a global array's size is fixed by the first set.
        Shader.SetGlobalVectorArray(PointsId, packedPoints);
        Shader.SetGlobalFloatArray(StrengthId, packedStrength);
        Shader.SetGlobalFloat(CountId, count);
        lastCount = count;
    }
}
