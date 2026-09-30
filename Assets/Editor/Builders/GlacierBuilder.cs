using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the third map - the glacier - into `Scenes/Glacier.unity`.
///
/// "Add a new glacier map thats all ice themed." The ground is a Terrain (TerrainKit) in snow,
/// packed ice and blue ice cliffs; everything on it is Kenney's Holiday Kit (Assets/Art/Glacier,
/// CC0 - see CREDITS.md): snowy pines, snow-capped rocks, snow walls, cabins built from its log
/// pieces, lanterns, sleds and an abandoned train. The colours are the kit's own, from its
/// colormap - nothing made up.
///
/// The layout, 130 x 130 m, the edge of it rising into ice cliffs:
/// - a frozen lake in the south-middle, flat and open - the dangerous way across;
/// - a crevasse across the middle, one across the south-east, both shallowing out at the ends to
///   climb in and out - somewhere to drop out of sight;
/// - an ice shelf in the north-west, a plateau with cliffs and two slopes up;
/// - an outpost of three log cabins in the north-east, fenced, lamp-lit - the close fighting;
/// - a ridge down the west, snowy hills in the south-west, ice pillars in the east and the middle
///   to swing between, and a train that came off its tracks in the south-east;
/// - twelve spawnpoints round the edge, all facing in.
///
/// No grass - it's ice. Refuses to run over an existing scene, the same as the zoo; Rebuild does.
/// </summary>
public static class GlacierBuilder
{
    public const string ScenePath = "Assets/Scenes/Glacier.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string Kit = "Assets/Art/Glacier/Models";
    const string SpawnpointPrefab = "Assets/Prefabs/World/Spawnpoint.prefab";
    const string TerrainPath = "Assets/Scenes/Terrain/Glacier.asset";
    const string GroundMaterialPath = "Assets/Scenes/Terrain/GlacierGround.mat";
    const string IceMaterialPath = "Assets/Art/Glacier/Ice.mat";
    const string SkyPath = "Assets/Art/Glacier/GlacierSky.mat";
    const string JungleSkyPath = "Assets/Resources/Sky/JungleSky.mat";

    const float Size = 130f;
    const float Half = Size * 0.5f;

    // The kit's pieces are 1m modules; three times that is a cabin a gorilla fits in.
    const float CabinScale = 3f;

    // The Holiday Kit's colormap, read off its swatches (Models/Textures/colormap.png).
    static readonly Color Snow = Hex(0xF1F8FD);
    static readonly Color PackedIce = Hex(0xB6D3F6);
    static readonly Color BlueIce = Hex(0x83ABE4);
    static readonly Color LakeIce = Hex(0x98BBEB);
    static readonly Color SkyTop = Hex(0x6386D3);
    static readonly Color SkyMid = Hex(0x98BBEB);
    static readonly Color SkyLow = Hex(0xE1F1FB);
    static readonly Color Cloud = Hex(0xC3CCF4);
    static readonly Color CloudLit = Hex(0xF1F8FD);

    static readonly Vector2 Lake = new Vector2(0f, -20f);
    const float LakeRadius = 12f;
    const float LakeDepth = -1.2f;

    static readonly Vector2 Shelf = new Vector2(-30f, 32f);
    const float ShelfHeight = 6f;

    static readonly Rect Outpost = Rect.MinMaxRect(16f, 14f, 48f, 45f);

    static readonly Vector2[] CrevasseMiddle = { new Vector2(-26f, 8f), new Vector2(-10f, 5f), new Vector2(4f, 11f), new Vector2(16f, 4f) };
    static readonly Vector2[] CrevasseSouth = { new Vector2(20f, -27f), new Vector2(32f, -31f), new Vector2(43f, -24f) };

    static readonly Vector2[] Pillars = { new Vector2(34f, -4f), new Vector2(44f, -14f), new Vector2(29f, -17f), new Vector2(-12f, -6f), new Vector2(8f, -4f) };

    // The train, off its tracks, south-east.
    static readonly Vector2 WreckFrom = new Vector2(16f, -44f);
    static readonly Vector2 WreckTo = new Vector2(42f, -50f);

    static readonly Vector2[][] Ways =
    {
        new[] { new Vector2(-24f, 13f), new Vector2(-26.5f, 22f) },    // up onto the shelf from the south
        new[] { new Vector2(-10f, 34f), new Vector2(-19.5f, 33f) },    // and from the east
    };

    static readonly Vector2[] SpawnSpots =
    {
        new Vector2(-44f, 46f), new Vector2(-8f, 50f), new Vector2(14f, 50f), new Vector2(51f, 30f),
        new Vector2(50f, 0f), new Vector2(52f, -40f), new Vector2(8f, -50f), new Vector2(-16f, -50f),
        new Vector2(-34f, -26f), new Vector2(-24f, 0f), new Vector2(12f, 20f), new Vector2(22f, -12f),
    };

    // The lobby camera's view (MapSetup.ViewSpots): from where it stands, past the gorilla, to the lake.
    static readonly Vector2[] LobbyView = { new Vector2(-10.5f, -37.5f), new Vector2(-5f, -27f) };

    static HeightMap ground;
    static readonly List<Vector3> taken = new List<Vector3>();

    [MenuItem("Tools/Gorilla Warfare/Build the glacier (once)")]
    public static void Run() => Build(false);

    [MenuItem("Tools/Gorilla Warfare/Rebuild the glacier (throws away hand edits)")]
    public static void Rebuild() => Build(true);

    static void Build(bool overwrite)
    {
        if (File.Exists(ScenePath) && !overwrite)
        {
            Fail($"{ScenePath} already exists - it's hand-edited from here. Rebuild runs over it.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Kit}/tree-snow-a.fbx") == null)
        {
            Fail($"no Holiday Kit models in {Kit}");
            return;
        }

        Scene glacier = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CopyFromGame(glacier);
        Weather();

        Transform map = new GameObject("Map").transform;
        taken.Clear();

        ground = ShapeGround();
        Material groundMaterial = TerrainKit.GroundMaterial(GroundMaterialPath, Snow, PackedIce, BlueIce, 0.18f);
        TerrainKit.Build(ground, map, TerrainPath, groundMaterial);

        BuildLake(map);
        BuildOutpost(map);
        BuildWreck(map);
        int dressing = Dress(map);
        BuildBoundary(map);
        BuildSpawns();

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(glacier, ScenePath);

        Debug.Log($"[glacier] built {ScenePath} - {map.GetComponentsInChildren<Renderer>().Length} pieces, {dressing} scattered, "
                  + $"{SpawnSpots.Length} spawnpoints. Run Tools/Gorilla Warfare/Set up the maps to put it in the lobby.");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- the scene

    /// The arena's sun and EventSystem - the same game - but not its grass: it's ice.
    static void CopyFromGame(Scene glacier)
    {
        Scene game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(game);
        UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
        float ambientIntensity = RenderSettings.ambientIntensity;
        string sunName = RenderSettings.sun != null ? RenderSettings.sun.name : null;

        Light sun = null;
        foreach (GameObject root in game.GetRootGameObjects())
        {
            if (root.name != "EventSystem" && root.GetComponent<Light>() == null)
                continue;

            GameObject copy = Object.Instantiate(root);
            copy.name = root.name;
            SceneManager.MoveGameObjectToScene(copy, glacier);

            if (copy.GetComponent<Light>() != null && (sun == null || root.name == sunName))
                sun = copy.GetComponent<Light>();
        }

        EditorSceneManager.CloseScene(game, true);
        SceneManager.SetActiveScene(glacier);

        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.sun = sun;
    }

    /// <summary>
    /// A colder day: the arena's own sky shader with the kit's ice blues in it and more cloud, a
    /// white sun, and a light haze of snow-coloured fog so the far side of the map fades back.
    /// </summary>
    static void Weather()
    {
        Material jungleSky = AssetDatabase.LoadAssetAtPath<Material>(JungleSkyPath);
        Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
        if (sky == null && jungleSky != null)
        {
            sky = new Material(jungleSky);
            AssetDatabase.CreateAsset(sky, SkyPath);
        }

        if (sky != null)
        {
            sky.SetColor("_ZenithColor", SkyTop);
            sky.SetColor("_SkyColor", SkyMid);
            sky.SetColor("_HorizonColor", SkyLow);
            sky.SetColor("_CloudColor", Cloud);
            sky.SetColor("_CloudColorLit", CloudLit);
            sky.SetFloat("_CloudCoverage", 0.58f);
            sky.SetColor("_SunColor", CloudLit);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = SkyLow;
        RenderSettings.fogDensity = 0.0085f;

        if (RenderSettings.sun != null)
            RenderSettings.sun.color = CloudLit;
    }

    // ---------------------------------------------------------------- the ground

    static HeightMap ShapeGround()
    {
        HeightMap g = new HeightMap(Size, 513, -10f, 40f);

        // Drifts.
        g.Noise(1.3f, 18f, 21);

        // The ice shelf, NW, and its two slopes.
        g.Mesa(Shelf, 11f, ShelfHeight, 2.4f, 0.2f, 14);
        g.Ramp(Ways[0][0], 0f, Ways[0][1], ShelfHeight, 4f, 2f);
        g.Ramp(Ways[1][0], 0f, Ways[1][1], ShelfHeight, 4f, 2f);

        // The ridge down the west, the hills south-west and north.
        g.Ridge(new Vector2(-46f, -38f), new Vector2(-44f, 4f), 8f, 5f);
        g.Hill(new Vector2(-30f, -44f), 12f, 4f);
        g.Hill(new Vector2(4f, 40f), 10f, 3f);

        // Pillars of ice - steep all round, for the vine.
        foreach (Vector2 pillar in Pillars)
            g.Mesa(pillar, 2.4f, 7f, 1.1f, 0.3f, (int)(pillar.x * 3f + pillar.y));

        // The glacier's own walls: up into ice cliffs all round.
        g.Rim(50f, 16f, 5f);

        g.Pad(Outpost, 0f, 4f);
        g.Pad(Rect.MinMaxRect(Mathf.Min(WreckFrom.x, WreckTo.x) - 2f, Mathf.Min(WreckFrom.y, WreckTo.y) - 3f,
                              Mathf.Max(WreckFrom.x, WreckTo.x) + 2f, Mathf.Max(WreckFrom.y, WreckTo.y) + 3f), 0f, 4f);
        g.Disc(Lake, LakeRadius, LakeDepth, 5f);

        // Every spawnpoint on a little level ground - nobody comes back on a slope, and there's a
        // flat few metres round you to fight from.
        foreach (Vector2 spot in SpawnSpots)
            g.Disc(spot, 3f, g.Height(spot.x, spot.y), 5f);

        // The crevasses last, so nothing flattens them back in.
        g.Trench(CrevasseMiddle, 2f, 1.2f, 5f, 7f);
        g.Trench(CrevasseSouth, 2f, 1.2f, 4.5f, 6f);

        return g;
    }

    /// The frozen lake: a sheet of glossy ice over the flat of the basin.
    static void BuildLake(Transform map)
    {
        Material ice = AssetDatabase.LoadAssetAtPath<Material>(IceMaterialPath);
        if (ice == null)
        {
            ice = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(ice, IceMaterialPath);
        }

        ice.color = LakeIce;
        ice.SetFloat("_Glossiness", 0.85f);
        EditorUtility.SetDirty(ice);

        GameObject sheet = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        sheet.name = "LakeIce";
        sheet.transform.SetParent(map, false);
        sheet.transform.localPosition = new Vector3(Lake.x, LakeDepth + 0.02f, Lake.y);
        sheet.transform.localScale = new Vector3(LakeRadius * 2f + 1f, 0.05f, LakeRadius * 2f + 1f);
        sheet.GetComponent<Renderer>().sharedMaterial = ice;
        Object.DestroyImmediate(sheet.GetComponent<Collider>());
        sheet.AddComponent<MeshCollider>().sharedMesh = sheet.GetComponent<MeshFilter>().sharedMesh;
    }

    // ---------------------------------------------------------------- the outpost

    /// Three log cabins, a fence round the north and east with gaps, lanterns, sleds and benches.
    static void BuildOutpost(Transform map)
    {
        Transform outpost = Group(map, "Outpost");

        Cabin(outpost, "Bunkhouse", new Vector2(22f, 19f), 3, 0f, doorAt: 1, chimney: false);
        Cabin(outpost, "Stores", new Vector2(37f, 19f), 2, 0f, doorAt: 0, chimney: true);
        Cabin(outpost, "Radio", new Vector2(40f, 33f), 3, 90f, doorAt: 1, chimney: true);

        // A fence along the back and the east side, a gap every few panels.
        for (int i = 0; i < 9; i++)
        {
            if (i % 3 == 2)
                continue;
            Piece(outpost, "cabin-fence", new Vector3(17f + i * 3.3f, 0f, 40f), 0f, CabinScale);
        }

        foreach ((string model, Vector2 at, float yaw, float scale) in new[]
                 {
                     ("lantern", new Vector2(19f, 16f), 0f, 1.4f), ("lantern", new Vector2(33f, 16f), 0f, 1.4f),
                     ("lantern", new Vector2(30f, 28f), 0f, 1.4f), ("lantern", new Vector2(44f, 27f), 0f, 1.4f),
                     ("lantern", new Vector2(21f, 38f), 0f, 1.4f),
                     ("sled", new Vector2(30f, 20f), 20f, 3f), ("sled-long", new Vector2(33f, 36f), 100f, 3f),
                     ("sled", new Vector2(20f, 32f), 250f, 3f),
                     ("bench", new Vector2(27f, 31f), 90f, 2.2f), ("bench", new Vector2(36f, 27f), 0f, 2.2f),
                     ("snow-bunker", new Vector2(25f, 34f), 30f, 3f), ("rocks-medium", new Vector2(46f, 16f), 60f, 1.8f),
                     ("tree-snow-b", new Vector2(45f, 40f), 10f, 4.5f), ("tree-snow-a", new Vector2(18f, 44f), 80f, 5f),
                 })
        {
            Piece(outpost, model, new Vector3(at.x, 0f, at.y), yaw, scale);
            taken.Add(new Vector3(at.x, at.y, 2f));
        }
    }

    /// <summary>
    /// A cabin two tiles wide and `length` long, on the kit's 1m grid scaled up: log walls, a
    /// corner post at each corner, windows down the long sides, a doorway in the front, a wooden
    /// floor and a snowy gable roof along its length. `corner` is the front-left tile's centre;
    /// `yaw` turns the whole cabin about it. Every piece is solid - walls stop bullets, the roof
    /// can be stood on.
    /// </summary>
    static void Cabin(Transform parent, string name, Vector2 corner, int length, float yaw, int doorAt, bool chimney)
    {
        Transform cabin = Group(parent, name);
        cabin.localPosition = new Vector3(corner.x, 0f, corner.y);
        cabin.localRotation = Quaternion.Euler(0f, yaw, 0f);

        const int width = 2;
        float s = CabinScale;

        void Local(string model, float x, float y, float z, float turn)
        {
            GameObject piece = TerrainKit.Prop(cabin, $"{Kit}/{model}.fbx", Vector3.zero, 0f, s);
            if (piece == null)
                return;
            piece.transform.localPosition = new Vector3(x * s, y * s, z * s);
            piece.transform.localRotation = Quaternion.Euler(0f, turn, 0f);
        }

        for (int ix = 0; ix < width; ix++)
        {
            for (int iz = 0; iz < length; iz++)
            {
                Local("floor-wood-snow", ix, 0f, iz, 0f);

                // The walls sit on a tile's +z edge unturned; a quarter turn puts them on +x, and so on.
                if (iz == 0)
                    Local(ix == doorAt ? "cabin-doorway" : "cabin-wall", ix, 0f, iz, 180f);
                if (iz == length - 1)
                    Local("cabin-wall", ix, 0f, iz, 0f);
                if (ix == width - 1)
                    Local(iz % 2 == 1 ? "cabin-window-a" : "cabin-wall", ix, 0f, iz, 90f);
                if (ix == 0)
                    Local(iz % 2 == 1 ? "cabin-window-a" : "cabin-wall", ix, 0f, iz, 270f);

                // Each half of the roof slopes from its ridge over the middle down past its own wall.
                bool stack = chimney && ix == 0 && iz == length - 1;
                Local(stack ? "cabin-roof-snow-chimney" : "cabin-roof-snow", ix, 1f, iz, ix == 0 ? 0f : 180f);
            }
        }

        Local("cabin-corner", width - 1, 0f, length - 1, 0f);
        Local("cabin-corner", width - 1, 0f, 0f, 90f);
        Local("cabin-corner", 0f, 0f, 0f, 180f);
        Local("cabin-corner", 0f, 0f, length - 1, 270f);
    }

    /// The train: four wagons strung out along where the track ran, the last two knocked askew.
    static void BuildWreck(Transform map)
    {
        Transform wreck = Group(map, "Wreck");
        Vector2 along = (WreckTo - WreckFrom).normalized;
        float yaw = Mathf.Atan2(along.x, along.y) * Mathf.Rad2Deg - 90f;
        string[] wagons = { "train-wagon-logs", "train-wagon-flat", "train-wagon-logs", "train-wagon-flat" };
        float[] askew = { 0f, 4f, 18f, -32f };

        for (int i = 0; i < wagons.Length; i++)
        {
            Vector2 at = Vector2.Lerp(WreckFrom, WreckTo, (i + 0.5f) / wagons.Length) + new Vector2(-along.y, along.x) * (i >= 2 ? 1.5f * (i - 1) : 0f);
            Piece(wreck, wagons[i], new Vector3(at.x, ground.Height(at.x, at.y), at.y), yaw + askew[i], 10f);
            taken.Add(new Vector3(at.x, at.y, 7f));
        }

        // Snow drifted up against it.
        foreach (Vector2 at in new[] { new Vector2(30f, -41f), new Vector2(38f, -44f) })
        {
            Piece(wreck, "snow-pile", new Vector3(at.x, ground.Height(at.x, at.y), at.y), at.x * 11f, 4f, false);
            taken.Add(new Vector3(at.x, at.y, 3f));
        }
    }

    // ---------------------------------------------------------------- dressing

    static readonly TerrainKit.Scatter Pines = new TerrainKit.Scatter
    {
        folder = Kit, models = new[] { "tree-snow-a", "tree-snow-b", "tree-snow-c" },
        scale = new Vector2(3f, 5.5f), spacing = 4.5f, maxSteep = 0.3f,
    };

    static readonly TerrainKit.Scatter Rocks = new TerrainKit.Scatter
    {
        folder = Kit, models = new[] { "rocks-large", "rocks-medium", "rocks-small" },
        scale = new Vector2(1.3f, 2.6f), spacing = 5f, maxSteep = 0.45f, sink = 0.08f,
    };

    static readonly TerrainKit.Scatter SnowWalls = new TerrainKit.Scatter
    {
        folder = Kit, models = new[] { "snow-bunker" },
        scale = new Vector2(2.6f, 3.4f), spacing = 7f, maxSteep = 0.2f, sink = 0.05f,
    };

    static readonly TerrainKit.Scatter Drifts = new TerrainKit.Scatter
    {
        folder = Kit, models = new[] { "snow-pile", "snow-flat", "snow-flat-large" },
        scale = new Vector2(3f, 5f), solid = false, spacing = 0f, maxSteep = 0.08f, sink = 0.02f, shadows = false,
    };

    /// Pines in stands - round the outpost, on the shelf, the ridge and the south-west hills - with
    /// rocks, snow walls to crouch behind and drifts everywhere; never on the lake, a slope up, a
    /// crevasse floor or a spawnpoint.
    static int Dress(Transform map)
    {
        Transform dressing = Group(map, "Dressing");
        System.Random random = new System.Random(20261002);
        Rect all = new Rect(-Half + 5f, -Half + 5f, Size - 10f, Size - 10f);

        bool Open(Vector2 at, float margin)
        {
            if ((at - Lake).magnitude < LakeRadius + 2f + margin)
                return false;

            // Nothing on the pillars' tops, where a drift hangs off the edge into thin air.
            foreach (Vector2 pillar in Pillars)
            {
                if ((at - pillar).magnitude < 4.5f + margin)
                    return false;
            }
            if (TerrainKit.InAny(at, new[] { Outpost }, margin))
                return false;
            if (TerrainKit.DistanceToLine(at, CrevasseMiddle, out _) < 3f + margin || TerrainKit.DistanceToLine(at, CrevasseSouth, out _) < 3f + margin)
                return false;

            foreach (Vector2[] way in Ways)
            {
                if (TerrainKit.DistanceToLine(at, way, out _) < 2.5f + margin)
                    return false;
            }

            if (TerrainKit.DistanceToLine(at, LobbyView, out _) < 3f + margin)
                return false;

            // A clearing round every spawnpoint, wider for a tree than a fern - you come back with
            // somewhere to look and shoot, not with a trunk in your face.
            float clearing = 3f + margin * 1.5f;
            foreach (Vector2 spawn in SpawnSpots)
            {
                if ((spawn - at).sqrMagnitude < clearing * clearing)
                    return false;
            }

            return true;
        }

        int placed = 0;
        foreach ((Vector2 centre, float radius, int count) in new[] { (new Vector2(-30f, 32f), 10f, 14), (new Vector2(-46f, -16f), 12f, 14),
                                                                      (new Vector2(-30f, -44f), 12f, 16), (new Vector2(4f, 42f), 10f, 10),
                                                                      (new Vector2(50f, 44f), 10f, 8), (new Vector2(52f, -2f), 8f, 6) })
        {
            placed += TerrainKit.Spread(dressing, ground, random, Pines, count, new Rect(centre - Vector2.one * radius, Vector2.one * radius * 2f),
                                        at => (at - centre).magnitude < radius && Open(at, 1f), taken);
        }

        placed += TerrainKit.Spread(dressing, ground, random, Pines, 20, all, at => Open(at, 2f), taken);
        placed += TerrainKit.Spread(dressing, ground, random, Rocks, 34, all, at => Open(at, 1f), taken);
        placed += TerrainKit.Spread(dressing, ground, random, SnowWalls, 14, Rect.MinMaxRect(-40f, -40f, 40f, 30f), at => Open(at, 1f), taken);
        placed += TerrainKit.Spread(dressing, ground, random, Drifts, 110, all, at => Open(at, 0f), null);

        return placed;
    }

    /// <summary>
    /// Invisible walls just inside the terrain's edge. The ice cliffs are too steep to walk up, but
    /// the vine can pull you up them - and past the top of the cliff there's no more world.
    /// </summary>
    static void BuildBoundary(Transform map)
    {
        Transform boundary = Group(map, "Boundary");
        float at = Half - 2f, height = 70f, y = height * 0.5f - 10f;

        foreach ((string name, Vector3 centre, Vector3 size) in new[]
                 {
                     ("North", new Vector3(0f, y, at), new Vector3(Size, height, 1f)),
                     ("South", new Vector3(0f, y, -at), new Vector3(Size, height, 1f)),
                     ("East", new Vector3(at, y, 0f), new Vector3(1f, height, Size)),
                     ("West", new Vector3(-at, y, 0f), new Vector3(1f, height, Size)),
                 })
        {
            GameObject wall = new GameObject(name);
            wall.transform.SetParent(boundary, false);
            wall.transform.localPosition = centre;
            wall.AddComponent<BoxCollider>().size = size;
        }
    }

    static void BuildSpawns()
    {
        GameObject host = new GameObject("SpawnManager");
        host.AddComponent<SpawnManager>();
        GameObject pad = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnpointPrefab);

        for (int i = 0; i < SpawnSpots.Length; i++)
        {
            Vector2 spot = SpawnSpots[i];
            Vector3 at = new Vector3(spot.x, ground.Height(spot.x, spot.y) + 1.1f, spot.y);
            GameObject point = (GameObject)PrefabUtility.InstantiatePrefab(pad, host.scene);
            point.name = $"Spawnpoint{i}";
            point.transform.SetParent(host.transform, false);
            point.transform.position = at;
            point.transform.rotation = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z).normalized, Vector3.up);
        }
    }

    // ---------------------------------------------------------------- pieces

    static Transform Group(Transform parent, string name)
    {
        Transform group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    static GameObject Piece(Transform parent, string model, Vector3 at, float yaw, float scale, bool solid = true)
    {
        return TerrainKit.Prop(parent, $"{Kit}/{model}.fbx", at, yaw, scale, solid);
    }

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    static void Fail(string why)
    {
        Debug.LogError("[glacier] " + why);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
