using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What the map builders shape their ground with: a Unity Terrain, made from a few plain features
/// rather than painted by hand - hills, flat-topped mesas with cliff sides, flat pads for buildings
/// to stand on, ramps up to high ground, trenches, and some noise so nothing is perfectly smooth.
///
/// Reported 2026-09-30: "try using the terrain builder instead of shapes and boxes for everything
/// so you can add some verticality to the map. Right now, the blocky cliffs and rocks arent it and
/// theyre more so decoration instead of being like a part of the map." So the high ground is the
/// ground itself now - walkable slopes up, cliffs you can't walk up, and the buildings on pads.
///
/// Coloured by Custom/MapTerrain from how steep the ground is, in colours taken from the kits:
/// level ground, a slope you can still walk up, and anything steeper than the player's 45 degree
/// slope limit (PlayerController.prefab's CharacterController) - so a cliff reads as a cliff.
///
/// The terrain is called "Floor", the name everything that looks for a map's ground already
/// searches for (MapSetup and MenuBuilder point the lobby's grass at it, GrassSetup finds it).
/// </summary>
public static class TerrainKit
{
    /// Steepness (1 - normal.y) where the shader's colours change. A 45 degree slope is 0.29 - the
    /// player's CharacterController won't climb past that, so that's where cliff colour starts.
    public const float BankFrom = 0.13f;
    public const float CliffFrom = 0.27f;

    /// Grass stops just before the bank colour starts, so it grows on what reads as grass.
    public const float GrassMaxSlope = 0.17f;

    /// <summary>
    /// The terrain: made (or reshaped, keeping its asset and so every reference to it) at
    /// `dataPath`, centred on the origin under `parent`.
    /// </summary>
    public static Terrain Build(HeightMap map, Transform parent, string dataPath, Material material)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dataPath));

        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (data == null)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
        }

        // Resolution before size: setting the resolution rescales the size to match it.
        data.heightmapResolution = map.Resolution;
        data.size = new Vector3(map.Size, map.Range, map.Size);

        // No painted layers - the colour comes from the shader - so the splat maps can be tiny.
        data.alphamapResolution = 16;
        data.baseMapResolution = 16;
        data.SetHeights(0, 0, map.Normalised());
        EditorUtility.SetDirty(data);

        GameObject host = Terrain.CreateTerrainGameObject(data);
        host.name = "Floor";
        host.transform.SetParent(parent, false);
        host.transform.position = new Vector3(-map.Size * 0.5f, map.Bottom, -map.Size * 0.5f);

        Terrain terrain = host.GetComponent<Terrain>();
        terrain.materialTemplate = material;

        // The shader is a plain surface shader: instanced terrain moves its vertices on the GPU
        // from the heightmap, which needs the built-in terrain shader's own code to do it.
        terrain.drawInstanced = false;

        // Walls and cut edges sit exactly on the heightmap; a coarse LOD would push the ground up
        // through them at a distance.
        terrain.heightmapPixelError = 1f;
        terrain.heightmapMaximumLOD = 0;
        terrain.basemapDistance = 2000f;
        terrain.drawTreesAndFoliage = false;
        terrain.allowAutoConnect = false;
        terrain.groupingID = -1;

        return terrain;
    }

    /// The terrain's colours, as a material asset beside its data so a scene can keep a reference.
    public static Material GroundMaterial(string path, Color flat, Color bank, Color cliff, float smoothness = 0.05f)
    {
        Shader shader = Shader.Find("Custom/MapTerrain");
        if (shader == null)
            throw new InvalidOperationException("no Custom/MapTerrain shader - Assets/Shaders/MapTerrain.shader");

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = shader;
        material.SetColor("_FlatColour", flat);
        material.SetColor("_BankColour", bank);
        material.SetColor("_CliffColour", cliff);
        material.SetFloat("_BankFrom", BankFrom);
        material.SetFloat("_CliffFrom", CliffFrom);
        material.SetFloat("_Glossiness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    /// Only grow grass where the ground reads as grass - see GrassMaxSlope.
    public static void GrassOnFlatOnly(GrassField grass)
    {
        SerializedObject so = new SerializedObject(grass);
        so.FindProperty("maxSlope").floatValue = GrassMaxSlope;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- props

    /// <summary>
    /// A model from a kit, with a real collider on every mesh when it's `solid` - the same way the
    /// maps have always placed them, rather than trusting the FBX's import settings. Undergrowth
    /// goes in without: a fern you can't walk through is a wall.
    /// </summary>
    public static GameObject Prop(Transform parent, string modelPath, Vector3 at, float yaw, float scale, bool solid = true,
                                  bool shadows = true)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

        if (source == null)
        {
            Debug.LogWarning($"[terrain] no model at {modelPath} - skipped");
            return null;
        }

        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(source, parent.gameObject.scene);
        prop.transform.SetParent(parent, false);
        prop.transform.position = at;
        prop.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        prop.transform.localScale = Vector3.one * scale;

        if (solid)
        {
            foreach (MeshFilter mesh in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mesh.sharedMesh != null && mesh.GetComponent<Collider>() == null)
                    mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
            }
        }

        // A map is a thousand of these, most sharing a handful of the kit's materials - static
        // batching draws them in a few batches instead of one each. Ankle-high plants cast no
        // shadow anyone would miss, and each one is another draw in the shadow pass.
        foreach (Transform part in prop.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(part.gameObject, StaticEditorFlags.BatchingStatic);

        if (!shadows)
        {
            foreach (Renderer view in prop.GetComponentsInChildren<Renderer>(true))
                view.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        return prop;
    }

    /// <summary>
    /// A kind of thing to scatter: which models, how big, whether it's solid, how far apart, and
    /// how steep the ground under it can be.
    /// </summary>
    public class Scatter
    {
        public string folder;
        public string[] models;
        public Vector2 scale = new Vector2(1f, 1f);
        public bool solid = true;
        public float spacing = 2f;
        public float maxSteep = 0.3f;
        public bool shadows = true;

        /// How far down into the ground it's pushed, as a fraction of its scale, so it doesn't
        /// stand on one edge on a slope.
        public float sink = 0.05f;
    }

    /// <summary>
    /// Up to `count` of a Scatter inside `area` wherever `allowed` says yes, the ground isn't too
    /// steep, and nothing already placed of the same spacing group is closer than `spacing`.
    /// Seeded, so a rebuild comes out the same. Returns how many went in.
    /// </summary>
    public static int Spread(Transform parent, HeightMap ground, System.Random random, Scatter kind, int count,
                             Rect area, Func<Vector2, bool> allowed, List<Vector3> taken)
    {
        int placed = 0;

        for (int attempt = 0; attempt < count * 12 && placed < count; attempt++)
        {
            Vector2 at = new Vector2(Mathf.Lerp(area.xMin, area.xMax, (float)random.NextDouble()),
                                     Mathf.Lerp(area.yMin, area.yMax, (float)random.NextDouble()));
            float yaw = (float)random.NextDouble() * 360f;
            float scale = Mathf.Lerp(kind.scale.x, kind.scale.y, (float)random.NextDouble());
            string model = kind.models[random.Next(kind.models.Length)];

            if (allowed != null && !allowed(at))
                continue;

            if (ground.Steepness(at.x, at.y) > kind.maxSteep)
                continue;

            if (TooClose(at, kind.spacing, taken))
                continue;

            float y = ground.Height(at.x, at.y) - kind.sink * scale;
            if (Prop(parent, $"{kind.folder}/{model}.fbx", new Vector3(at.x, y, at.y), yaw, scale, kind.solid, kind.shadows) == null)
                continue;

            taken?.Add(new Vector3(at.x, at.y, kind.spacing));
            placed++;
        }

        return placed;
    }

    /// Each taken spot keeps its own spacing in z, so a tree keeps a bush further off than a bush does.
    static bool TooClose(Vector2 at, float spacing, List<Vector3> taken)
    {
        if (taken == null)
            return false;

        foreach (Vector3 other in taken)
        {
            float gap = Mathf.Max(spacing, other.z) * 0.5f + Mathf.Min(spacing, other.z) * 0.5f;
            if ((new Vector2(other.x, other.y) - at).sqrMagnitude < gap * gap)
                return true;
        }

        return false;
    }

    /// Whether a point is inside any of these rectangles, grown by `margin`.
    public static bool InAny(Vector2 at, IEnumerable<Rect> rects, float margin = 0f)
    {
        foreach (Rect r in rects)
        {
            if (at.x > r.xMin - margin && at.x < r.xMax + margin && at.y > r.yMin - margin && at.y < r.yMax + margin)
                return true;
        }

        return false;
    }

    /// Distance from a point to a polyline, in the XZ plane.
    public static float DistanceToLine(Vector2 p, IReadOnlyList<Vector2> line, out float along)
    {
        float best = float.MaxValue;
        along = 0f;
        float travelled = 0f;

        for (int i = 0; i + 1 < line.Count; i++)
        {
            Vector2 a = line[i], b = line[i + 1];
            Vector2 ab = b - a;
            float length = ab.magnitude;
            float t = length > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / (length * length)) : 0f;
            float d = (a + ab * t - p).magnitude;

            if (d < best)
            {
                best = d;
                along = travelled + t * length;
            }

            travelled += length;
        }

        return best;
    }

    public static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}

/// <summary>
/// A square heightfield in world metres, centred on the origin, that TerrainKit.Build turns into a
/// Terrain. Features are applied in order; the ones that flatten (pads, ramps) go last so what
/// stands on them is level.
/// </summary>
public class HeightMap
{
    public readonly float Size;
    public readonly int Resolution;

    /// World height of the lowest the ground can go, and how far above that the highest can be.
    public readonly float Bottom;
    public readonly float Range;

    readonly float[,] heights;   // world metres, [z, x]

    public HeightMap(float size, int resolution, float bottom, float range, float ground = 0f)
    {
        Size = size;
        Resolution = resolution;
        Bottom = bottom;
        Range = range;
        heights = new float[resolution, resolution];

        for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
                heights[z, x] = ground;
    }

    public float Spacing => Size / (Resolution - 1);

    float Coordinate(int i) => -Size * 0.5f + i * Spacing;

    /// Changes every sample: `change(x, z, height)` returns the new height.
    public void Apply(Func<float, float, float, float> change)
    {
        for (int z = 0; z < Resolution; z++)
        {
            float wz = Coordinate(z);
            for (int x = 0; x < Resolution; x++)
                heights[z, x] = change(Coordinate(x), wz, heights[z, x]);
        }
    }

    /// The ground's height anywhere, between samples the same way the terrain draws it.
    public float Height(float x, float z)
    {
        float fx = Mathf.Clamp((x + Size * 0.5f) / Spacing, 0f, Resolution - 1.001f);
        float fz = Mathf.Clamp((z + Size * 0.5f) / Spacing, 0f, Resolution - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;

        float bottom = Mathf.Lerp(heights[iz, ix], heights[iz, ix + 1], tx);
        float top = Mathf.Lerp(heights[iz + 1, ix], heights[iz + 1, ix + 1], tx);
        return Mathf.Lerp(bottom, top, tz);
    }

    /// 1 - normal.y, the same measure the shader colours by - 0 flat, 0.29 at 45 degrees.
    public float Steepness(float x, float z)
    {
        float d = Spacing;
        float dx = (Height(x + d, z) - Height(x - d, z)) / (2f * d);
        float dz = (Height(x, z + d) - Height(x, z - d)) / (2f * d);
        return 1f - 1f / Mathf.Sqrt(1f + dx * dx + dz * dz);
    }

    public float[,] Normalised()
    {
        float[,] result = new float[Resolution, Resolution];
        for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
                result[z, x] = Mathf.Clamp01((heights[z, x] - Bottom) / Range);
        return result;
    }

    // ---------------------------------------------------------------- features

    /// A rounded hill, `height` at the middle and nothing past `radius`.
    public void Hill(Vector2 centre, float radius, float height)
    {
        Apply((x, z, h) =>
        {
            float d = (new Vector2(x, z) - centre).magnitude / radius;
            return d >= 1f ? h : h + height * TerrainKit.Smooth(1f - d);
        });
    }

    /// A long rounded ridge between two points.
    public void Ridge(Vector2 from, Vector2 to, float radius, float height)
    {
        Vector2[] line = { from, to };
        Apply((x, z, h) =>
        {
            float d = TerrainKit.DistanceToLine(new Vector2(x, z), line, out _) / radius;
            return d >= 1f ? h : h + height * TerrainKit.Smooth(1f - d);
        });
    }

    /// <summary>
    /// A flat top at `height` out to `radius`, falling over `cliff` metres - steep when the fall is
    /// short against the height. The edge wobbles by `wobble` (a fraction of the radius) so it
    /// isn't a perfect circle. Raises only: a mesa never digs into higher ground round it.
    /// </summary>
    public void Mesa(Vector2 centre, float radius, float height, float cliff, float wobble = 0.15f, int seed = 1)
    {
        float phase = seed * 1.7f;
        Apply((x, z, h) =>
        {
            Vector2 offset = new Vector2(x, z) - centre;
            float angle = Mathf.Atan2(offset.y, offset.x);
            float edge = radius * (1f + wobble * (0.6f * Mathf.Sin(angle * 3f + phase) + 0.4f * Mathf.Sin(angle * 5f - phase * 2f)));
            float w = 1f - TerrainKit.Smooth((offset.magnitude - edge) / cliff);
            return Mathf.Max(h, Mathf.Lerp(h, height, w));
        });
    }

    /// Level ground at `height` inside the rectangle, blending back into what's round it over `blend`.
    public void Pad(Rect area, float height, float blend)
    {
        Apply((x, z, h) =>
        {
            float dx = Mathf.Max(area.xMin - x, 0f, x - area.xMax);
            float dz = Mathf.Max(area.yMin - z, 0f, z - area.yMax);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return Mathf.Lerp(h, height, 1f - TerrainKit.Smooth(d / Mathf.Max(0.01f, blend)));
        });
    }

    /// Round level ground.
    public void Disc(Vector2 centre, float radius, float height, float blend)
    {
        Apply((x, z, h) =>
        {
            float d = Mathf.Max(0f, (new Vector2(x, z) - centre).magnitude - radius);
            return Mathf.Lerp(h, height, 1f - TerrainKit.Smooth(d / Mathf.Max(0.01f, blend)));
        });
    }

    /// <summary>
    /// A straight walkable slope from one height to another, `width` wide, blending out at the
    /// sides over `blend`. Keep the rise under the run (45 degrees) or it's a cliff.
    /// </summary>
    public void Ramp(Vector2 from, float fromHeight, Vector2 to, float toHeight, float width, float blend)
    {
        Vector2 run = to - from;
        float length = run.magnitude;
        Vector2 along = run / length;

        Apply((x, z, h) =>
        {
            Vector2 p = new Vector2(x, z) - from;
            float t = Vector2.Dot(p, along);
            float side = Mathf.Abs(p.x * along.y - p.y * along.x);

            // Past the ends it fades too, so the ramp meets the ground at each end rather than
            // stopping in a wall.
            float beyond = Mathf.Max(-t, t - length, 0f);
            float d = Mathf.Sqrt(Mathf.Max(0f, side - width * 0.5f) * Mathf.Max(0f, side - width * 0.5f) + beyond * beyond);
            float w = 1f - TerrainKit.Smooth(d / Mathf.Max(0.01f, blend));

            float target = Mathf.Lerp(fromHeight, toHeight, Mathf.Clamp01(t / length));
            return Mathf.Lerp(h, target, w);
        });
    }

    /// <summary>
    /// A channel along a line - a river bed, a ravine, a crevasse: `depth` below whatever's there
    /// at its middle, flat-bottomed across `floor`, the sides rising over `bank`. With `taper`, it
    /// shallows out over that many metres at each end, so the ends are ways in and out.
    /// </summary>
    public void Trench(IReadOnlyList<Vector2> line, float floor, float bank, float depth, float taper = 0f)
    {
        float length = 0f;
        for (int i = 0; i + 1 < line.Count; i++)
            length += (line[i + 1] - line[i]).magnitude;

        Apply((x, z, h) =>
        {
            float d = TerrainKit.DistanceToLine(new Vector2(x, z), line, out float along);
            float w = 1f - TerrainKit.Smooth((d - floor * 0.5f) / Mathf.Max(0.01f, bank));
            if (taper > 0f)
                w *= TerrainKit.Smooth(Mathf.Min(along, length - along) / taper);
            return h - depth * w;
        });
    }

    /// <summary>
    /// Everything inside the rectangle set to exactly `height` with no blend at all - a sheer step,
    /// for a cut that something built hides the edge of (the zoo's pool, lined with its tiled
    /// walls). The step sits within one sample of the rectangle's edge.
    /// </summary>
    public void Cut(Rect area, float height)
    {
        Apply((x, z, h) => area.Contains(new Vector2(x, z)) ? height : h);
    }

    /// Gentle lumps everywhere, `amplitude` metres at most, `scale` metres across.
    public void Noise(float amplitude, float scale, int seed, Func<float, float, float> mask = null)
    {
        float ox = seed * 13.37f, oz = seed * 7.91f;
        Apply((x, z, h) =>
        {
            float n = Mathf.PerlinNoise(x / scale + ox, z / scale + oz) - 0.5f;
            n += 0.5f * (Mathf.PerlinNoise(x / (scale * 0.45f) + oz, z / (scale * 0.45f) + ox) - 0.5f);
            return h + n * 2f * amplitude * (mask != null ? mask(x, z) : 1f);
        });
    }

    /// <summary>
    /// Ground rising towards the edges of the map: from `from` metres out from the middle (measured
    /// square, the same shape as the walls) to `height` at the edge, steepening as it goes.
    /// </summary>
    public void Rim(float from, float height, float roughness = 0f, int seed = 3)
    {
        float half = Size * 0.5f;
        Apply((x, z, h) =>
        {
            float out_ = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (out_ <= from)
                return h;

            float t = Mathf.Clamp01((out_ - from) / (half - from));
            float rough = roughness > 0f ? (Mathf.PerlinNoise(x * 0.08f + seed, z * 0.08f - seed) - 0.5f) * roughness : 0f;
            return h + height * t * t + rough * t;
        });
    }
}
