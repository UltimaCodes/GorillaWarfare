using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the second map - a greybox zoo - into `Scenes/Zoo.unity`, once.
///
/// "Make a proprietary second map right now temporarily, ill fix it up (make it a zoo)." So this
/// is a layout, not a finished map: plain blocks in the jungle kit's own materials (stone, wood,
/// dirt - embedded in its models, nothing made up), the kit's trees and rocks as dressing, and
/// the arena's sun, sky and grass copied across so it looks like the same game. Real zoo art gets
/// sourced once the layout is right.
///
/// Refuses to run if the scene exists - from the first build on it's Ryaan's to edit by hand,
/// the same rule as MenuBuilder.Run. Delete the scene to build it again.
///
/// The layout, 90 x 90 m inside the walls, centred on the origin:
/// - a paved plaza in the middle with a bandstand to fight round and on;
/// - four avenues running out from it to the walls, lined with trees - the long sightlines;
/// - an enclosure in each corner, each a different shape of fight: a drained pool (NE) you drop
///   into, a rock enclosure (SE) to climb, a reptile house (SW) with a roof over it, and a tall
///   aviary cage (NW) to swing through on the vine;
/// - two spawnpoints per enclosure, none in the plaza, all facing in.
/// </summary>
public static class ZooBuilder
{
    public const string ScenePath = "Assets/Scenes/Zoo.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string Models = "Assets/Art/Jungle/Models";
    const string FloorMaterialPath = "Assets/Art/Jungle/Materials/grass.mat";
    const string SpawnpointPrefab = "Assets/Prefabs/World/Spawnpoint.prefab";

    const float Half = 45f;
    const float WallHeight = 8f;
    const float WallThickness = 1.5f;
    const float FloorDepth = 3f;

    // The drained pool's hole in the floor, NE.
    static readonly Rect Pool = new Rect(19f, 21f, 18f, 14f);
    const float PoolDepth = 3f;

    static Dictionary<string, Material> kit;
    static Material floorMaterial;

    [MenuItem("Tools/Gorilla Warfare/Build the zoo (greybox, once)")]
    public static void Run()
    {
        if (File.Exists(ScenePath))
        {
            Fail($"{ScenePath} already exists - it's hand-edited from here. Delete it to build it again.");
            return;
        }

        kit = KitMaterials();
        floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);

        foreach (string needed in new[] { "stone", "stoneDark", "dirt", "woodBark", "woodDark" })
        {
            if (!kit.ContainsKey(needed))
            {
                Fail($"the jungle kit has no '{needed}' material - found: {string.Join(", ", kit.Keys)}");
                return;
            }
        }

        if (floorMaterial == null)
        {
            Fail($"no floor material at {FloorMaterialPath}");
            return;
        }

        Scene zoo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GrassField grass = CopyFromGame(zoo);

        Transform map = new GameObject("Map").transform;

        List<Collider> ground = BuildFloor(map);
        BuildWalls(map);
        BuildPlaza(map);
        BuildAvenues(map);
        BuildPool(map);
        BuildRocks(map);
        BuildReptileHouse(map);
        BuildAviary(map);
        BuildSpawns();

        if (grass != null)
            PointGrassAt(grass, ground);

        EditorSceneManager.SaveScene(zoo, ScenePath);
        AddToBuildSettings(ScenePath);

        Debug.Log($"[zoo] built {ScenePath} - {map.GetComponentsInChildren<Renderer>().Length} pieces, "
                  + "8 spawnpoints. It's yours to edit from here; this won't run over it.");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// The zoo from above, and from eye height in a few places - to look at the layout rather
    /// than trust the numbers. Edit mode, so no grass (it grows at runtime). Output:
    /// Logs/zoo-shots/*.png.
    /// </summary>
    [MenuItem("Tools/Gorilla Warfare/Photograph the zoo")]
    public static void Photograph()
    {
        if (!File.Exists(ScenePath))
        {
            Fail($"no {ScenePath} to photograph");
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "zoo-shots");
        Directory.CreateDirectory(folder);

        GameObject host = new GameObject("~ZooCamera");
        Camera camera = host.AddComponent<Camera>();
        camera.farClipPlane = 400f;

        void Shot(string name, Vector3 at, Vector3 lookAt, bool fromAbove, float fov = 70f)
        {
            camera.orthographic = fromAbove;
            camera.orthographicSize = Half + 6f;
            camera.clearFlags = fromAbove ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = fov;
            host.transform.position = at;
            host.transform.rotation = Quaternion.LookRotation(lookAt - at, fromAbove ? Vector3.forward : Vector3.up);

            int width = fromAbove ? 1400 : 1920, height = fromAbove ? 1400 : 1080;
            RenderTexture target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
        }

        // GW_ZOO_VIEWS="x,y,z,yaw;..." - menu-camera framings (55 degrees, 16:9) to choose the lobby
        // backdrop's spot by looking, the way the arena's was chosen.
        string views = System.Environment.GetEnvironmentVariable("GW_ZOO_VIEWS");
        if (!string.IsNullOrEmpty(views))
        {
            string[] spots = views.Split(';');
            for (int i = 0; i < spots.Length; i++)
            {
                string[] v = spots[i].Split(',');
                if (v.Length < 4)
                    continue;

                float Parse(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                Vector3 at = new Vector3(Parse(v[0]), Parse(v[1]), Parse(v[2]));
                Vector3 look = at + Quaternion.Euler(-2f, Parse(v[3]), 0f) * Vector3.forward * 10f;

                Shot($"view-{i}", at, look, false, 55f);
            }

            Object.DestroyImmediate(host);
            Debug.Log($"[zoo] photographed {spots.Length} candidate views into {folder}");
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
            return;
        }

        Shot("top", new Vector3(0f, 120f, 0f), Vector3.zero, true);
        Shot("plaza-north", new Vector3(0f, 1.7f, -12f), new Vector3(0f, 2f, 20f), false);
        Shot("from-pool-spawn", new Vector3(16f, 1.7f, 40f), new Vector3(0f, 1f, 0f), false);
        Shot("into-pool", new Vector3(24f, 2.5f, 17f), new Vector3(30f, -2f, 30f), false);
        Shot("aviary-inside", new Vector3(-30f, 1.7f, 23f), new Vector3(-26f, 9f, 34f), false);
        Shot("reptile-house", new Vector3(-28f, 1.7f, -12f), new Vector3(-28f, 2f, -30f), false);
        Shot("rock-enclosure", new Vector3(16f, 1.7f, -16f), new Vector3(28f, 3f, -28f), false);

        Object.DestroyImmediate(host);
        Debug.Log($"[zoo] photographed into {folder}");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- shared with the arena

    /// The arena's sun, EventSystem and grass, and its sky and ambient light - so the zoo reads as
    /// the same game, and so that any lighting change to the arena is one re-copy away.
    static GrassField CopyFromGame(Scene zoo)
    {
        Scene game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);

        SceneManager.SetActiveScene(game);
        Material skybox = RenderSettings.skybox;
        UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
        float ambientIntensity = RenderSettings.ambientIntensity;
        Color ambientLight = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog;
        Color fogColour = RenderSettings.fogColor;
        FogMode fogMode = RenderSettings.fogMode;
        float fogDensity = RenderSettings.fogDensity;
        string sunName = RenderSettings.sun != null ? RenderSettings.sun.name : null;

        Light sun = null;
        GrassField grass = null;

        foreach (GameObject root in game.GetRootGameObjects())
        {
            bool wanted = root.name == "Grass" || root.name == "EventSystem" || root.GetComponent<Light>() != null;
            if (!wanted)
                continue;

            GameObject copy = Object.Instantiate(root);
            copy.name = root.name;
            SceneManager.MoveGameObjectToScene(copy, zoo);

            if (copy.GetComponent<Light>() != null && (sun == null || root.name == sunName))
                sun = copy.GetComponent<Light>();

            if (grass == null)
                grass = copy.GetComponent<GrassField>();
        }

        EditorSceneManager.CloseScene(game, true);
        SceneManager.SetActiveScene(zoo);

        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientLight = ambientLight;
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColour;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.sun = sun;

        return grass;
    }

    static void PointGrassAt(GrassField grass, List<Collider> ground)
    {
        SerializedObject so = new SerializedObject(grass);

        SerializedProperty list = so.FindProperty("ground");
        list.arraySize = ground.Count;
        for (int i = 0; i < ground.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = ground[i];

        so.FindProperty("area").rectValue = new Rect(-Half, -Half, Half * 2f, Half * 2f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- the ground

    /// A thick slab with the pool cut out of it - four pieces round the hole. Thick so the pool's
    /// sides are solid, not a floor over nothing.
    static List<Collider> BuildFloor(Transform map)
    {
        Transform floor = Group(map, "Floor");
        List<Collider> ground = new List<Collider>();

        void Slab(string name, float xMin, float xMax, float zMin, float zMax)
        {
            GameObject piece = Block(floor, name,
                new Vector3((xMin + xMax) * 0.5f, -FloorDepth * 0.5f, (zMin + zMax) * 0.5f),
                new Vector3(xMax - xMin, FloorDepth, zMax - zMin), floorMaterial);
            ground.Add(piece.GetComponent<Collider>());
        }

        Slab("South", -Half, Half, -Half, Pool.yMin);
        Slab("North", -Half, Half, Pool.yMax, Half);
        Slab("West", -Half, Pool.xMin, Pool.yMin, Pool.yMax);
        Slab("East", Pool.xMax, Half, Pool.yMin, Pool.yMax);

        return ground;
    }

    static void BuildWalls(Transform map)
    {
        Transform walls = Group(map, "Walls");
        Material stone = kit["stoneDark"];
        float span = Half * 2f + WallThickness * 2f;
        float at = Half + WallThickness * 0.5f;
        float y = WallHeight * 0.5f;

        Block(walls, "North", new Vector3(0f, y, at), new Vector3(span, WallHeight, WallThickness), stone);
        Block(walls, "South", new Vector3(0f, y, -at), new Vector3(span, WallHeight, WallThickness), stone);
        Block(walls, "East", new Vector3(at, y, 0f), new Vector3(WallThickness, WallHeight, span), stone);
        Block(walls, "West", new Vector3(-at, y, 0f), new Vector3(WallThickness, WallHeight, span), stone);
    }

    // ---------------------------------------------------------------- the middle

    /// Paved, open to the sky, with a bandstand in the centre - a raised ring to fight round,
    /// on and under, with a roof the vine can catch.
    static void BuildPlaza(Transform map)
    {
        Transform plaza = Group(map, "Plaza");

        Block(plaza, "Paving", new Vector3(0f, 0.01f, 0f), new Vector3(28f, 0.1f, 28f), kit["stone"]);

        Transform stand = Group(plaza, "Bandstand");
        Disc(stand, "Platform", new Vector3(0f, 0.6f, 0f), 9f, 1.2f, kit["stone"]);

        // Steps on two sides - 1.2m is a jump, not a walk.
        Block(stand, "StepsNorth", new Vector3(0f, 0.3f, 5f), new Vector3(3f, 0.6f, 1.6f), kit["stone"]);
        Block(stand, "StepsSouth", new Vector3(0f, 0.3f, -5f), new Vector3(3f, 0.6f, 1.6f), kit["stone"]);

        for (int i = 0; i < 6; i++)
        {
            float angle = i * 60f * Mathf.Deg2Rad;
            Disc(stand, $"Post{i}", new Vector3(Mathf.Cos(angle) * 4f, 3.2f, Mathf.Sin(angle) * 4f),
                 0.35f, 4f, kit["woodBark"]);
        }

        Disc(stand, "Roof", new Vector3(0f, 5.4f, 0f), 10f, 0.4f, kit["woodDark"]);

        // Low planters at the plaza's corners - cover without closing the sightlines off.
        foreach (Vector2 corner in new[] { new Vector2(10f, 10f), new Vector2(-10f, 10f),
                                           new Vector2(10f, -10f), new Vector2(-10f, -10f) })
        {
            Block(plaza, "Planter", new Vector3(corner.x, 0.5f, corner.y), new Vector3(3f, 1f, 3f), kit["stone"]);
            Prop(plaza, "plant_bushLarge", new Vector3(corner.x, 1f, corner.y), 0f, 3f);
        }
    }

    /// Dirt paths out from the plaza to the walls, trees down both sides, a few rocks for cover.
    static void BuildAvenues(Transform map)
    {
        Transform avenues = Group(map, "Avenues");
        Random.State saved = Random.state;
        Random.InitState(20260928);

        (Vector3 along, Vector3 across, string name)[] ways =
        {
            (Vector3.forward, Vector3.right, "North"), (Vector3.back, Vector3.right, "South"),
            (Vector3.right, Vector3.forward, "East"), (Vector3.left, Vector3.forward, "West"),
        };

        string[] trees = { "tree_tall", "tree_default", "tree_detailed", "tree_palmTall", "tree_palmDetailedTall" };

        foreach ((Vector3 along, Vector3 across, string name) in ways)
        {
            Transform avenue = Group(avenues, name);

            Vector3 middle = along * (14f + (Half - 14f) * 0.5f);
            Vector3 size = new Vector3(Mathf.Abs(across.x) * 6f + Mathf.Abs(along.x) * (Half - 14f), 0.1f,
                                       Mathf.Abs(across.z) * 6f + Mathf.Abs(along.z) * (Half - 14f));
            Block(avenue, "Path", middle + Vector3.up * 0.01f, size, kit["dirt"]);

            for (float d = 18f; d < Half - 3f; d += 8f)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 at = along * d + across * side * 11f;
                    Prop(avenue, trees[Random.Range(0, trees.Length)], at, Random.Range(0f, 360f), Random.Range(5f, 8f));
                }
            }

            // Two rocks in each avenue, off the path - something to duck behind on a long lane.
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = along * Random.Range(20f, Half - 6f) + across * (i == 0 ? -6f : 6f);
                Prop(avenue, i == 0 ? "rock_largeA" : "rock_tallA", at, Random.Range(0f, 360f), Random.Range(3f, 4.5f));
            }
        }

        // A statue at the end of the north avenue, so there's something to call out.
        Prop(avenues, "statue_head", new Vector3(0f, 0f, Half - 4f), 180f, 4f);

        Random.state = saved;
    }

    // ---------------------------------------------------------------- the enclosures

    /// NE. A drained pool: a 3m drop into a stone basin, a ramp back out along one side, and a
    /// railing round part of the rim.
    static void BuildPool(Transform map)
    {
        Transform pool = Group(map, "Pool");
        Material stone = kit["stone"];

        Block(pool, "Basin", new Vector3(Pool.center.x, -PoolDepth - 0.25f, Pool.center.y),
              new Vector3(Pool.width, 0.5f, Pool.height), stone);

        // Tiled sides over the floor slab's cut faces.
        float y = -PoolDepth * 0.5f;
        Block(pool, "SideNorth", new Vector3(Pool.center.x, y, Pool.yMax - 0.15f), new Vector3(Pool.width, PoolDepth, 0.3f), stone);
        Block(pool, "SideSouth", new Vector3(Pool.center.x, y, Pool.yMin + 0.15f), new Vector3(Pool.width, PoolDepth, 0.3f), stone);
        Block(pool, "SideEast", new Vector3(Pool.xMax - 0.15f, y, Pool.center.y), new Vector3(0.3f, PoolDepth, Pool.height), stone);
        Block(pool, "SideWest", new Vector3(Pool.xMin + 0.15f, y, Pool.center.y), new Vector3(0.3f, PoolDepth, Pool.height), stone);

        // Out along the west side, north to south: from the rim down to the basin.
        Ramp(pool, "Ramp", new Vector3(Pool.xMin + 2f, 0f, Pool.yMax - 0.5f),
             new Vector3(Pool.xMin + 2f, -PoolDepth, Pool.yMax - 9f), 3f, stone);

        // Cover in the basin, so dropping in isn't only a way to die.
        Block(pool, "LifeguardChair", new Vector3(Pool.center.x + 3f, -PoolDepth + 1f, Pool.center.y), new Vector3(2f, 2f, 2f), kit["woodDark"]);
        Block(pool, "DrainCover", new Vector3(Pool.center.x - 2f, -PoolDepth + 0.4f, Pool.center.y - 3f), new Vector3(3f, 0.8f, 1.5f), stone);

        // A railing on the plaza-facing edges, with gaps to get through.
        Material wood = kit["woodBark"];
        Block(pool, "RailSouthWest", new Vector3(Pool.xMin + 4f, 0.5f, Pool.yMin - 0.4f), new Vector3(8f, 1f, 0.2f), wood);
        Block(pool, "RailSouthEast", new Vector3(Pool.xMax - 3f, 0.5f, Pool.yMin - 0.4f), new Vector3(6f, 1f, 0.2f), wood);
        Block(pool, "RailWest", new Vector3(Pool.xMin - 0.4f, 0.5f, Pool.center.y - 2f), new Vector3(0.2f, 1f, 8f), wood);
    }

    /// SE. A rock enclosure - a stepped mound of the kit's cliff blocks to climb, a fence round the
    /// plaza-facing sides with gaps in it, and trees.
    static void BuildRocks(Transform map)
    {
        Transform rocks = Group(map, "RockEnclosure");
        Vector3 centre = new Vector3(28f, 0f, -28f);

        // Three tiers, 3m blocks, each smaller than the last.
        int[] widths = { 3, 2, 1 };
        for (int tier = 0; tier < widths.Length; tier++)
        {
            int n = widths[tier];
            for (int x = 0; x < n; x++)
            {
                for (int z = 0; z < n; z++)
                {
                    Vector3 at = centre + new Vector3((x - (n - 1) * 0.5f) * 3f, tier * 2.6f, (z - (n - 1) * 0.5f) * 3f);
                    Prop(rocks, "cliff_block_rock", at, 90f * ((x + z + tier) % 4), 3f);
                }
            }
        }

        // A slope up onto the first tier from the plaza side.
        Prop(rocks, "cliff_blockSlope_rock", centre + new Vector3(-6f, 0f, 0f), 90f, 3f);

        Prop(rocks, "tree_tall_dark", centre + new Vector3(8f, 0f, -8f), 20f, 7f);
        Prop(rocks, "tree_detailed_dark", centre + new Vector3(-8f, 0f, -9f), 140f, 6f);
        Prop(rocks, "rock_largeB", centre + new Vector3(9f, 0f, 6f), 60f, 4f);

        Fence(rocks, new Vector3(14.5f, 0f, -14.5f), new Vector3(14.5f, 0f, -42f));
        Fence(rocks, new Vector3(14.5f, 0f, -14.5f), new Vector3(42f, 0f, -14.5f));
    }

    /// SW. A reptile house - a stone building with a roof, open along the front where the glass
    /// will go, a door at the back, and tanks inside for cover.
    static void BuildReptileHouse(Transform map)
    {
        Transform house = Group(map, "ReptileHouse");
        Material stone = kit["stone"];
        float x0 = -38f, x1 = -18f, z0 = -35f, z1 = -21f, height = 6f, t = 0.6f;
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

        // Back wall with a door in the middle.
        Block(house, "BackLeft", new Vector3(cx - 6f, height * 0.5f, z0), new Vector3(8f, height, t), stone);
        Block(house, "BackRight", new Vector3(cx + 6f, height * 0.5f, z0), new Vector3(8f, height, t), stone);
        Block(house, "BackLintel", new Vector3(cx, height - 1f, z0), new Vector3(4f, 2f, t), stone);

        Block(house, "West", new Vector3(x0, height * 0.5f, cz), new Vector3(t, height, z1 - z0), stone);
        Block(house, "East", new Vector3(x1, height * 0.5f, cz), new Vector3(t, height, z1 - z0), stone);
        Block(house, "Roof", new Vector3(cx, height + 0.25f, cz), new Vector3(x1 - x0 + t, 0.5f, z1 - z0 + t), kit["stoneDark"]);

        // The front, facing the west avenue: pillars and nothing between them yet.
        for (int i = 0; i <= 4; i++)
            Block(house, $"Pillar{i}", new Vector3(Mathf.Lerp(x0, x1, i / 4f), height * 0.5f, z1), new Vector3(0.6f, height, 0.6f), stone);

        // Tanks along the back, and one in the middle.
        for (int i = 0; i < 3; i++)
            Block(house, $"Tank{i}", new Vector3(cx - 6f + i * 6f, 0.6f, z0 + 2f), new Vector3(3.5f, 1.2f, 2f), kit["woodDark"]);
        Block(house, "CentreTank", new Vector3(cx, 0.6f, cz + 1f), new Vector3(4f, 1.2f, 2f), kit["woodDark"]);
    }

    /// NW. A tall aviary - a cage of posts and roof beams 14m up, perches at two heights and a
    /// tree in the middle. The roof beams are what the vine is for.
    static void BuildAviary(Transform map)
    {
        Transform aviary = Group(map, "Aviary");
        Material wood = kit["woodBark"];
        float x0 = -38f, x1 = -18f, z0 = 18f, z1 = 38f, height = 14f, step = 2.5f;

        // Posts round the edge, with a gap in the middle of the two plaza-facing sides.
        for (float x = x0; x <= x1 + 0.01f; x += step)
        {
            if (Mathf.Abs(x - (x0 + x1) * 0.5f) > 2f)
                Block(aviary, "Post", new Vector3(x, height * 0.5f, z0), new Vector3(0.25f, height, 0.25f), wood);
            Block(aviary, "Post", new Vector3(x, height * 0.5f, z1), new Vector3(0.25f, height, 0.25f), wood);
        }

        for (float z = z0 + step; z < z1 - 0.01f; z += step)
        {
            Block(aviary, "Post", new Vector3(x0, height * 0.5f, z), new Vector3(0.25f, height, 0.25f), wood);
            if (Mathf.Abs(z - (z0 + z1) * 0.5f) > 2f)
                Block(aviary, "Post", new Vector3(x1, height * 0.5f, z), new Vector3(0.25f, height, 0.25f), wood);
        }

        // The roof grid.
        for (float x = x0; x <= x1 + 0.01f; x += step)
            Block(aviary, "Beam", new Vector3(x, height, (z0 + z1) * 0.5f), new Vector3(0.3f, 0.3f, z1 - z0), wood);
        for (float z = z0; z <= z1 + 0.01f; z += step)
            Block(aviary, "Beam", new Vector3((x0 + x1) * 0.5f, height, z), new Vector3(x1 - x0, 0.3f, 0.3f), wood);

        // Perches: two low, one high, so there's somewhere to land from a swing.
        Block(aviary, "PerchLowA", new Vector3(x0 + 5f, 4.5f, z0 + 5f), new Vector3(4f, 0.4f, 4f), kit["woodDark"]);
        Block(aviary, "PerchLowB", new Vector3(x1 - 5f, 4.5f, z1 - 5f), new Vector3(4f, 0.4f, 4f), kit["woodDark"]);
        Block(aviary, "PerchHigh", new Vector3(x0 + 5f, 9f, z1 - 5f), new Vector3(4f, 0.4f, 4f), kit["woodDark"]);

        Prop(aviary, "tree_tall", new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f), 0f, 9f);
    }

    /// Two per enclosure, never in the plaza or the pool, facing the middle of the map.
    static void BuildSpawns()
    {
        GameObject host = new GameObject("SpawnManager");
        host.AddComponent<SpawnManager>();
        GameObject pad = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnpointPrefab);

        Vector2[] spots =
        {
            new Vector2(16f, 40f), new Vector2(40f, 17f),      // pool
            new Vector2(18f, -40f), new Vector2(40f, -18f),    // rocks
            new Vector2(-28f, -31f), new Vector2(-40f, -17f),  // reptile house, inside and out
            new Vector2(-30f, 23f), new Vector2(-16f, 40f),    // aviary, inside and out
        };

        for (int i = 0; i < spots.Length; i++)
        {
            Vector3 at = new Vector3(spots[i].x, 1.1f, spots[i].y);
            GameObject point = (GameObject)PrefabUtility.InstantiatePrefab(pad, host.scene);
            point.name = $"Spawnpoint{i}";
            point.transform.SetParent(host.transform, false);
            point.transform.position = at;
            point.transform.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z).normalized, Vector3.up);
        }
    }

    // ---------------------------------------------------------------- pieces

    static Dictionary<string, Material> KitMaterials()
    {
        Dictionary<string, Material> found = new Dictionary<string, Material>();

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Models }))
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
            {
                if (asset is Material material && !found.ContainsKey(material.name))
                    found[material.name] = material;
            }
        }

        return found;
    }

    static Transform Group(Transform parent, string name)
    {
        Transform group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    static GameObject Block(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.SetParent(parent, false);
        block.transform.localPosition = centre;
        block.transform.localScale = size;
        block.GetComponent<Renderer>().sharedMaterial = material;
        return block;
    }

    /// A cylinder with a real mesh collider - the primitive's own capsule collider is the wrong
    /// shape for anything flatter or taller than a pill.
    static GameObject Disc(Transform parent, string name, Vector3 centre, float diameter, float height, Material material)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = name;
        disc.transform.SetParent(parent, false);
        disc.transform.localPosition = centre;
        disc.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        disc.GetComponent<Renderer>().sharedMaterial = material;

        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.AddComponent<MeshCollider>().sharedMesh = disc.GetComponent<MeshFilter>().sharedMesh;
        return disc;
    }

    /// A walkable slab from one point's top surface to another's.
    static void Ramp(Transform parent, string name, Vector3 from, Vector3 to, float width, Material material)
    {
        Vector3 run = to - from;
        const float thickness = 0.4f;

        GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = name;
        ramp.transform.SetParent(parent, false);
        ramp.transform.localRotation = Quaternion.LookRotation(run.normalized, Vector3.up);
        ramp.transform.localPosition = (from + to) * 0.5f - ramp.transform.localRotation * Vector3.up * (thickness * 0.5f);
        ramp.transform.localScale = new Vector3(width, thickness, run.magnitude);
        ramp.GetComponent<Renderer>().sharedMaterial = material;
    }

    /// Wooden fence posts and a rail between two points, leaving a 4m gap every 12m.
    static void Fence(Transform parent, Vector3 from, Vector3 to)
    {
        Transform fence = Group(parent, "Fence");
        Material wood = kit["woodBark"];
        float length = Vector3.Distance(from, to);
        Vector3 direction = (to - from).normalized;
        Quaternion facing = Quaternion.LookRotation(direction, Vector3.up);

        for (float d = 0f; d < length; d += 12f)
        {
            float segment = Mathf.Min(8f, length - d);
            if (segment <= 0.5f)
                break;

            Vector3 mid = from + direction * (d + segment * 0.5f);
            GameObject rail = Block(fence, "Rail", mid + Vector3.up * 0.7f, new Vector3(0.2f, 1.4f, segment), wood);
            rail.transform.localRotation = facing;
        }
    }

    /// One of the jungle kit's models, with a real collider on every mesh - the same way
    /// MapExpansion places them, rather than trusting the FBX's import settings.
    static GameObject Prop(Transform parent, string model, Vector3 at, float yaw, float scale)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{model}.fbx");

        if (source == null)
        {
            Debug.LogWarning($"[zoo] no {model} in the jungle kit - skipped");
            return null;
        }

        GameObject prop = (GameObject)PrefabUtility.InstantiatePrefab(source, parent.gameObject.scene);
        prop.transform.SetParent(parent, false);
        prop.transform.localPosition = at;
        prop.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        prop.transform.localScale = Vector3.one * scale;

        foreach (MeshFilter mesh in prop.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.GetComponent<Collider>() == null)
                mesh.gameObject.AddComponent<MeshCollider>().sharedMesh = mesh.sharedMesh;
        }

        return prop;
    }

    static void AddToBuildSettings(string path)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        foreach (EditorBuildSettingsScene scene in scenes)
        {
            if (scene.path == path)
                return;
        }

        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void Fail(string why)
    {
        Debug.LogError("[zoo] " + why);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
