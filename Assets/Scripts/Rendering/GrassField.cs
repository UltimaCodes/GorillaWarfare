using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grows the grass on the map's floor when a match loads, and hands it to MinionsArt's
/// GrassComputeScript (Assets/Grass, see its CREDIT.txt) to draw on the GPU.
///
/// Generated here rather than painted into the scene with the Grass Tool because
/// GrassComputeScript serializes every point it's given into the scene file - a whole floor is
/// tens of thousands of entries, megabytes of YAML rewritten in git on every touch-up. A fixed
/// seed means it's the same field on every client and every launch anyway. Anything painted with
/// the tool still wins: if the scene already holds grass data, this leaves it alone.
///
/// Found by raycasting down onto the floor's own colliders rather than reading its mesh, because
/// mesh data is only readable at runtime when the import has Read/Write on - which works in the
/// Editor either way and then fails in a build.
///
/// Sized against the player rather than the tool's defaults (a metre tall, a metre wide - half a
/// gorilla). First pass was 0.28-0.45 m, "shin height" on the 2 m capsule - but a gorilla's legs are
/// short, and an enemy five metres off was hidden to the knee. 0.2-0.35 m sits around the ankle to
/// low shin: enough to read as a field you're wading through, not enough to hide a leg hitbox.
/// </summary>
[RequireComponent(typeof(GrassComputeScript))]
public class GrassField : MonoBehaviour
{
    public static GrassField Instance { get; private set; }

    [Tooltip("The floor's colliders. Grass only grows where a ray from above lands on one of these.")]
    [SerializeField] Collider[] ground;

    [Tooltip("World XZ rectangle to grow inside - the arena inside its walls. Empty means the whole floor.")]
    [SerializeField] Rect area;

    [Tooltip("Same seed, same field - on every client, every launch.")]
    [SerializeField] int seed = 20260927;

    // The arena inside the walls is about 126 x 124 m. The cost is the draw buffer, which has to
    // hold every triangle at once: 60k points x 20 triangles (4 blades, 3 segments) is ~100 MB.
    [SerializeField] float pointsPerSquareMetre = 5f;
    [SerializeField] int maxPoints = 60000;

    [Tooltip("Blade height range in metres. The player capsule is 2 m tall, but a gorilla's legs are short.")]
    [SerializeField] float minHeight = 0.2f;
    [SerializeField] float maxHeight = 0.35f;

    [Tooltip("Half the blade's width - GrassBlades.compute builds each blade from -width to +width.")]
    [SerializeField] float bladeWidth = 0.045f;

    [Tooltip("Per-point colour multiplier around white; the actual greens come from the grass settings' tints.")]
    [SerializeField] float colourVariation = 0.12f;

    [Tooltip("How far from flat a surface can be and still grow grass (0 = only perfectly flat).")]
    [SerializeField] float maxSlope = 0.3f;

    [Tooltip("What grass won't grow through - trunks, rocks, cliffs.")]
    [SerializeField] LayerMask blockedBy = 1;

    public int PointCount { get; private set; }

    readonly Collider[] overlap = new Collider[8];

    void Awake()
    {
        Instance = this;

        // GrassComputeScript culls and fades against a camera, and Camera.main finds nothing here -
        // the player camera is deliberately untagged (see PlayerController).
        GrassComputeScript.PlayModeCamera = () => PlayerController.LocalCamera;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Start, not Awake: GrassComputeScript's own OnEnable has run by now (and found nothing to
    // draw), so Reset() below is the one real setup rather than a second one.
    void Start()
    {
        GrassComputeScript compute = GetComponent<GrassComputeScript>();

        if (compute.SetGrassPaintedDataList.Count > 0)
        {
            PointCount = compute.SetGrassPaintedDataList.Count;
            return;
        }

        List<GrassData> points = Generate();
        PointCount = points.Count;

        compute.SetGrassPaintedDataList = points;
        compute.Reset();
    }

    public List<GrassData> Generate()
    {
        List<GrassData> points = new List<GrassData>();

        if (ground == null || ground.Length == 0)
            return points;

        Bounds floor = ground[0].bounds;
        foreach (Collider c in ground)
        {
            if (c != null)
                floor.Encapsulate(c.bounds);
        }

        // The floor plane runs well past the walls; grass out there is triangles nobody sees.
        Rect grow = area.width > 0f && area.height > 0f
            ? area
            : Rect.MinMaxRect(floor.min.x, floor.min.z, floor.max.x, floor.max.z);

        int attempts = Mathf.Min(maxPoints, Mathf.CeilToInt(grow.width * grow.height * pointsPerSquareMetre));
        float rayStart = floor.max.y + 5f;
        float rayLength = floor.size.y + 10f;
        float minNormalY = 1f - maxSlope;

        System.Random random = new System.Random(seed);

        for (int i = 0; i < attempts; i++)
        {
            Vector3 top = new Vector3(Mathf.Lerp(grow.xMin, grow.xMax, Next(random)), rayStart,
                                      Mathf.Lerp(grow.yMin, grow.yMax, Next(random)));

            // The random draws happen whether or not this point is kept, so every client consumes
            // the sequence identically and grows the same field.
            float heightRoll = Next(random);
            float widthRoll = Next(random);
            Vector3 colourRoll = new Vector3(Next(random), Next(random), Next(random));

            if (!HitGround(new Ray(top, Vector3.down), rayLength, out RaycastHit hit) || hit.normal.y < minNormalY)
                continue;

            if (Blocked(hit.point))
                continue;

            float shade = 1f - colourVariation * 0.5f;

            points.Add(new GrassData
            {
                position = hit.point,
                normal = hit.normal,
                length = new Vector2(bladeWidth * Mathf.Lerp(0.8f, 1.2f, widthRoll), Mathf.Lerp(minHeight, maxHeight, heightRoll)),
                color = new Vector3(shade, shade, shade) + colourRoll * colourVariation,
            });
        }

        return points;
    }

    bool HitGround(Ray ray, float length, out RaycastHit best)
    {
        best = default;
        bool found = false;

        foreach (Collider c in ground)
        {
            if (c != null && c.Raycast(ray, out RaycastHit hit, length) && (!found || hit.distance < best.distance))
            {
                best = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// A small box from just above the ground up past the blades' height, clear of the floor itself -
    /// so it finds a trunk, rock or cliff standing on this spot, and anything lying on the floor too:
    /// paving, a path, a building's floor. It used to start 18cm up, so a 10cm paving slab passed
    /// under it and the zoo's plaza and paths grew grass straight through the concrete.
    /// </summary>
    bool Blocked(Vector3 point)
    {
        int count = Physics.OverlapBoxNonAlloc(point + Vector3.up * 0.22f, new Vector3(0.12f, 0.2f, 0.12f), overlap,
                                               Quaternion.identity, blockedBy, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (System.Array.IndexOf(ground, overlap[i]) < 0)
                return true;
        }

        return false;
    }

    static float Next(System.Random random) => (float)random.NextDouble();
}
