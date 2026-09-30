using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the second map - a zoo, overgrown - into `Scenes/Zoo.unity`, once.
///
/// "Make a proprietary second map right now temporarily, ill fix it up (make it a zoo)." So this
/// is a layout, not a finished map: the buildings are plain blocks in the jungle kit's own
/// materials (stone, wood, dirt - embedded in its models, nothing made up), the kit's trees,
/// plants and rocks are the dressing, and the arena's sun, sky and grass are copied across so it
/// looks like the same game. Real zoo art gets sourced once the layout is right.
///
/// Refuses to run if the scene exists - from the first build on it's Ryaan's to edit by hand,
/// the same rule as MenuBuilder.Run. Rebuild runs over it, for as long as nobody has (git history
/// is the check - on 2026-09-30 only the build commit had ever touched it).
///
/// Rebuilt 2026-09-30, three reports the same day:
/// - "expand the map, make it 1.5 times bigger (and i dont mean enlarge it, i mean expand it),
///   make the border walls taller" - 135 x 135 m, was 90; the middle kept as it was and a ring of
///   new ground round it; walls 14m over the ground, were 8.
/// - "try using the terrain builder instead of shapes and boxes... the blocky cliffs and rocks
///   arent it" - the ground is a Terrain (TerrainKit) now. The rock enclosure is a two-tier hill,
///   not a stack of cliff blocks; the lawns roll; there are knolls, a hollow and a ridge.
/// - "make it feel like a proper jungle" - the zoo's gone wild: trees, bushes, ferns and flowers
///   all over the lawns, bamboo in the hollow, plants growing in the drained pool.
///
/// The layout, centred on the origin:
/// - a paved plaza in the middle with a bandstand to fight round and on;
/// - four avenues running out from it to the walls, lined with trees - the long sightlines;
/// - an enclosure in each inner corner, each a different shape of fight: a drained pool (NE) you
///   drop into, a rock hill (SE) to climb, a reptile house (SW) with a roof over it, and a tall
///   aviary cage (NW) to swing through on the vine;
/// - round them, the new ground: an elephant house the east avenue runs through, a giraffe
///   paddock with two feeding towers either side of the west avenue, a cafe on the north side, a
///   monkey climbing frame and a keeper's hut on the south, and in the outer corners a water tower,
///   a bamboo hollow, a rocky knoll and a wooded hill;
/// - fourteen spawnpoints, none in the plaza, all facing in.
///
/// Grass grows only on the lawns: paving, paths and every building's floor keep it off (see
/// GrassField.Blocked), and so does ground too steep to read as grass - it grew through the plaza's
/// concrete, reported the same day.
/// </summary>
public static class ZooBuilder
{
    public const string ScenePath = "Assets/Scenes/Zoo.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string Models = "Assets/Art/Jungle/Models";
    const string FloorMaterialPath = "Assets/Art/Jungle/Materials/grass.mat";
    const string SpawnpointPrefab = "Assets/Prefabs/World/Spawnpoint.prefab";
    const string TerrainPath = "Assets/Scenes/Terrain/Zoo.asset";
    const string GroundMaterialPath = "Assets/Scenes/Terrain/ZooGround.mat";

    const float Half = 67.5f;
    const float WallTop = 16f;     // 14m over the ground where it banks up against the walls
    const float WallBottom = -2f;
    const float WallThickness = 1.5f;

    // Where the old 90m map ended - the enclosures there still sit inside it, and the new areas go
    // outside it.
    const float InnerHalf = 45f;

    // The drained pool's hole in the ground, NE.
    static readonly Rect Pool = new Rect(19f, 21f, 18f, 14f);
    const float PoolDepth = 3f;

    // The rock hill, SE: a lower shelf and the top.
    static readonly Vector2 RockHill = new Vector2(28f, -28f);
    const float ShelfHeight = 3.2f;
    const float TopHeight = 7f;

    static Dictionary<string, Material> kit;
    static HeightMap ground;
    static readonly List<Vector3> taken = new List<Vector3>();

    [MenuItem("Tools/Gorilla Warfare/Build the zoo (once)")]
    public static void Run() => Build(false);

    /// Builds it again over the existing scene, keeping its GUID - and throwing away anything done
    /// to it by hand. Only while nothing has been.
    [MenuItem("Tools/Gorilla Warfare/Rebuild the zoo (throws away hand edits)")]
    public static void Rebuild() => Build(true);

    static void Build(bool overwrite)
    {
        if (File.Exists(ScenePath) && !overwrite)
        {
            Fail($"{ScenePath} already exists - it's hand-edited from here. Rebuild runs over it.");
            return;
        }

        kit = KitMaterials();
        Material grass = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);

        foreach (string needed in new[] { "stone", "stoneDark", "dirt", "woodBark", "woodDark" })
        {
            if (!kit.ContainsKey(needed))
            {
                Fail($"the jungle kit has no '{needed}' material - found: {string.Join(", ", kit.Keys)}");
                return;
            }
        }

        if (grass == null)
        {
            Fail($"no floor material at {FloorMaterialPath}");
            return;
        }

        Scene zoo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GrassField grassField = CopyFromGame(zoo);

        Transform map = new GameObject("Map").transform;
        taken.Clear();

        // The kit's own colours: the arena floor's grass on the level, its dirt on a slope you can
        // still climb, its dark stone on a cliff you can't.
        ground = ShapeGround();
        Material groundMaterial = TerrainKit.GroundMaterial(GroundMaterialPath, grass.color, kit["dirt"].color, kit["stoneDark"].color);
        Terrain terrain = TerrainKit.Build(ground, map, TerrainPath, groundMaterial);

        BuildWalls(map);
        BuildPlaza(map);
        BuildAvenues(map);
        BuildPool(map);
        BuildRockHill(map);
        BuildReptileHouse(map);
        BuildAviary(map);
        BuildElephantHouse(map);
        BuildGiraffePaddock(map);
        BuildCafe(map);
        BuildMonkeyFrame(map);
        BuildKeepersHut(map);
        BuildCorners(map);
        int wild = Overgrow(map);
        BuildSpawns();

        if (grassField != null)
        {
            PointGrassAt(grassField, new List<Collider> { terrain.GetComponent<TerrainCollider>() });
            TerrainKit.GrassOnFlatOnly(grassField);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(zoo, ScenePath);
        AddToBuildSettings(ScenePath);

        Debug.Log($"[zoo] built {ScenePath} - {map.GetComponentsInChildren<Renderer>().Length} pieces, {wild} of them growing wild, "
                  + $"{SpawnSpots.Length} spawnpoints. It's yours to edit from here; Run won't go over it.");

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

    static void PointGrassAt(GrassField grass, List<Collider> colliders)
    {
        SerializedObject so = new SerializedObject(grass);

        SerializedProperty list = so.FindProperty("ground");
        list.arraySize = colliders.Count;
        for (int i = 0; i < colliders.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];

        so.FindProperty("area").rectValue = new Rect(-Half, -Half, Half * 2f, Half * 2f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- the ground

    // Everything built stands level at 0 on these - the paths, the plaza, every building's
    // footprint - and nothing grows wild on them.
    static readonly Rect[] Pads =
    {
        Rect.MinMaxRect(-14f, -14f, 14f, 14f),                 // plaza
        Rect.MinMaxRect(-3.5f, 14f, 3.5f, Half),               // avenues
        Rect.MinMaxRect(-3.5f, -Half, 3.5f, -14f),
        Rect.MinMaxRect(14f, -3.5f, Half, 3.5f),
        Rect.MinMaxRect(-Half, -3.5f, -14f, 3.5f),
        Rect.MinMaxRect(16.5f, 18.5f, 39.5f, 37.5f),           // round the pool
        Rect.MinMaxRect(-38.5f, 17.5f, -17.5f, 38.5f),         // aviary
        Rect.MinMaxRect(-39f, -36f, -17f, -20f),               // reptile house
        Rect.MinMaxRect(48f, -12f, 65f, 12f),                  // elephant house
        Rect.MinMaxRect(-61f, 9.5f, -41.5f, 16.5f),            // giraffe towers and their ramps
        Rect.MinMaxRect(-61f, -16.5f, -41.5f, -9.5f),
        Rect.MinMaxRect(-66f, 3f, -48f, 5f),                   // paddock fences
        Rect.MinMaxRect(-66f, -5f, -48f, -3f),
        Rect.MinMaxRect(13f, 45f, 35f, 65f),                   // cafe and terrace
        Rect.MinMaxRect(7f, -63f, 37f, -49f),                  // monkey frame and its ramp
        Rect.MinMaxRect(-39f, -63f, -25f, -51f),               // keeper's hut
        Rect.MinMaxRect(40f, 52f, 60f, 60f),                   // water tower and its ramp
        Rect.MinMaxRect(13.5f, -43f, 15.5f, -14f),             // rock hill fences
        Rect.MinMaxRect(14f, -15.5f, 43f, -13.5f),
    };

    // The slopes up onto high ground: the rock hill's, and the knoll's. Kept clear of anything growing.
    static readonly Vector2[][] Ways =
    {
        new[] { new Vector2(44f, -41f), RockHill + new Vector2(5f, -5f) },
        new[] { new Vector2(-43f, -61f), new Vector2(-51.5f, -58.5f) },
    };

    /// <summary>
    /// Rolling lawns; the rock hill; the knolls and hollows in the outer ring; the ground banking
    /// up against the walls; then everything built flattened level, and the pool cut in last.
    /// </summary>
    static HeightMap ShapeGround()
    {
        HeightMap g = new HeightMap(Half * 2f, 513, -8f, 32f);

        g.Noise(1.1f, 16f, 7);

        // SE, the rock hill: a skirt, a shelf to jump up onto from the plaza side, and the top -
        // cliffs all round but the long dirt slope up from the far side.
        g.Hill(RockHill, 14f, 1.5f);
        g.Mesa(RockHill + new Vector2(-1f, 1f), 10.5f, ShelfHeight, 1.8f, 0.18f, 2);
        g.Mesa(RockHill + new Vector2(1.5f, -1.5f), 6.5f, TopHeight, 2.2f, 0.2f, 5);
        g.Ramp(Ways[0][0], 0f, Ways[0][1], TopHeight, 4f, 2.5f);

        // Between the enclosures and the avenues: a ridge either side of the north avenue, a low
        // mound west of the plaza.
        g.Ridge(new Vector2(-9f, 19f), new Vector2(-9f, 40f), 5.5f, 2.2f);
        g.Ridge(new Vector2(10f, 18f), new Vector2(10f, 40f), 4.5f, 1.6f);
        g.Hill(new Vector2(-28f, -8f), 8f, 1.8f);

        // The outer ring.
        g.Hill(new Vector2(-24f, 56f), 14f, 5f);                       // N, a big grassy hill
        g.Mesa(new Vector2(-24f, 56f), 4f, 5.4f, 3f, 0.2f, 9);         // with a flat lookout
        g.Hill(new Vector2(-12f, -56f), 9f, 3f);                       // S
        g.Ridge(new Vector2(58f, 18f), new Vector2(58f, 42f), 7f, 4f); // E, north of the elephant house
        g.Hill(new Vector2(52f, -30f), 10f, 3f);                       // E, south of it
        g.Hill(new Vector2(-56f, 30f), 8f, 2.5f);                      // W, under the palms
        g.Hill(new Vector2(-56f, -30f), 8f, 2.5f);
        g.Hill(new Vector2(56f, -56f), 12f, 3.5f);                     // SE corner, the wooded hill
        g.Mesa(new Vector2(-57f, -57f), 7f, 6f, 2.2f, 0.25f, 4);       // SW corner, the rocky knoll
        g.Ramp(Ways[1][0], 0f, Ways[1][1], 6f, 3.5f, 2f);
        g.Disc(new Vector2(-56f, 56f), 6f, -1.8f, 7f);                 // NW corner, the bamboo hollow

        g.Rim(Half - 6f, 1.5f, 1f);

        foreach (Rect pad in Pads)
            g.Pad(pad, 0f, 3f);

        // Every spawnpoint on a little level ground - nobody comes back on a slope, and there's a
        // flat few metres round you to fight from.
        foreach (Vector2 spot in SpawnSpots)
            g.Disc(spot, 3f, g.Height(spot.x, spot.y), 5f);

        // The pool floor sits 20cm under the basin's slab, so the two never fight over the same
        // plane; the tiled walls stand over the step at the edge.
        g.Cut(Pool, -PoolDepth - 0.2f);

        return g;
    }

    static void BuildWalls(Transform map)
    {
        Transform walls = Group(map, "Walls");
        Material stone = kit["stoneDark"];
        float span = Half * 2f + WallThickness * 2f;
        float at = Half + WallThickness * 0.5f;
        float height = WallTop - WallBottom;
        float y = (WallTop + WallBottom) * 0.5f;

        Block(walls, "North", new Vector3(0f, y, at), new Vector3(span, height, WallThickness), stone);
        Block(walls, "South", new Vector3(0f, y, -at), new Vector3(span, height, WallThickness), stone);
        Block(walls, "East", new Vector3(at, y, 0f), new Vector3(WallThickness, height, span), stone);
        Block(walls, "West", new Vector3(-at, y, 0f), new Vector3(WallThickness, height, span), stone);
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

        // Low planters at the plaza's corners - cover without closing the sightlines off - gone to
        // seed like everything else.
        foreach (Vector2 corner in new[] { new Vector2(10f, 10f), new Vector2(-10f, 10f),
                                           new Vector2(10f, -10f), new Vector2(-10f, -10f) })
        {
            Block(plaza, "Planter", new Vector3(corner.x, 0.5f, corner.y), new Vector3(3f, 1f, 3f), kit["stone"]);
            Prop(plaza, "plant_bushLarge", new Vector3(corner.x, 1f, corner.y), 0f, 6f, false);
            Prop(plaza, "plant_flatTall", new Vector3(corner.x + 0.8f, 1f, corner.y - 0.6f), 40f, 2.2f, false);
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

            // The east avenue ends inside the elephant house, so its trees stop short of the doors.
            float treesTo = name == "East" ? 46f : Half - 3f;

            for (float d = 18f; d < treesTo; d += 8f)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 at = along * d + across * side * 11f;
                    Grown(avenue, trees[Random.Range(0, trees.Length)], at, Random.Range(0f, 360f), Random.Range(5f, 8f));
                }
            }

            // Rocks off the path - something to duck behind on a long lane. Two on the old stretch,
            // one more on the new.
            for (int i = 0; i < 3; i++)
            {
                float d = i < 2 ? Random.Range(20f, InnerHalf - 6f) : Random.Range(InnerHalf + 2f, 47f);
                Vector3 at = along * d + across * (i % 2 == 0 ? -6f : 6f);
                Grown(avenue, i == 1 ? "rock_tallA" : "rock_largeA", at, Random.Range(0f, 360f), Random.Range(3f, 4.5f));
            }
        }

        // A statue at the end of the north avenue, so there's something to call out.
        Prop(avenues, "statue_head", new Vector3(0f, 0f, Half - 4f), 180f, 4f);

        Random.state = saved;
    }

    // ---------------------------------------------------------------- the enclosures

    /// NE. A drained pool: a 3m drop into a stone basin, a ramp back out along one side, a railing
    /// round part of the rim - and the jungle getting into it.
    static void BuildPool(Transform map)
    {
        Transform pool = Group(map, "Pool");
        Material stone = kit["stone"];

        // The slab's top is the pool floor; the ground's cut sits 20cm below it.
        Block(pool, "Basin", new Vector3(Pool.center.x, -PoolDepth - 0.3f, Pool.center.y),
              new Vector3(Pool.width + 0.8f, 0.6f, Pool.height + 0.8f), stone);

        // Tiled walls standing over the ground's cut edge, half in and half out of it - the step in
        // the heightmap sits within a sample of the edge, and these are thick enough to cover it.
        const float t = 0.8f;
        float y = (-PoolDepth + 0.05f) * 0.5f;
        float h = PoolDepth + 0.05f;
        Block(pool, "SideNorth", new Vector3(Pool.center.x, y, Pool.yMax), new Vector3(Pool.width + t, h, t), stone);
        Block(pool, "SideSouth", new Vector3(Pool.center.x, y, Pool.yMin), new Vector3(Pool.width + t, h, t), stone);
        Block(pool, "SideEast", new Vector3(Pool.xMax, y, Pool.center.y), new Vector3(t, h, Pool.height + t), stone);
        Block(pool, "SideWest", new Vector3(Pool.xMin, y, Pool.center.y), new Vector3(t, h, Pool.height + t), stone);

        // Out along the west side, north to south: from the rim down to the basin.
        Ramp(pool, "Ramp", new Vector3(Pool.xMin + 2.4f, 0.05f, Pool.yMax - 0.4f),
             new Vector3(Pool.xMin + 2.4f, -PoolDepth, Pool.yMax - 9f), 3f, stone);

        // Cover in the basin, so dropping in isn't only a way to die.
        Block(pool, "LifeguardChair", new Vector3(Pool.center.x + 3f, -PoolDepth + 1f, Pool.center.y), new Vector3(2f, 2f, 2f), kit["woodDark"]);
        Block(pool, "DrainCover", new Vector3(Pool.center.x - 2f, -PoolDepth + 0.4f, Pool.center.y - 3f), new Vector3(3f, 0.8f, 1.5f), stone);

        // Growing up through the cracks.
        foreach ((string model, Vector2 at, float size) in new[] { ("plant_bushLarge", new Vector2(33f, 32f), 5.5f),
                                                                    ("plant_flatTall", new Vector2(29f, 24f), 3.5f),
                                                                    ("grass_leafsLarge", new Vector2(25f, 31f), 3.5f),
                                                                    ("plant_bushDetailed", new Vector2(35f, 23f), 5f),
                                                                    ("grass_large", new Vector2(31f, 28f), 3f) })
            Prop(pool, model, new Vector3(at.x, -PoolDepth, at.y), at.x * 37f, size, false);
        Prop(pool, "tree_thin", new Vector3(34.5f, -PoolDepth, 32.5f), 70f, 5f);

        // A railing on the plaza-facing edges, with gaps to get through.
        Material wood = kit["woodBark"];
        Block(pool, "RailSouthWest", new Vector3(Pool.xMin + 4f, 0.5f, Pool.yMin - 0.6f), new Vector3(8f, 1f, 0.2f), wood);
        Block(pool, "RailSouthEast", new Vector3(Pool.xMax - 3f, 0.5f, Pool.yMin - 0.6f), new Vector3(6f, 1f, 0.2f), wood);
        Block(pool, "RailWest", new Vector3(Pool.xMin - 0.6f, 0.5f, Pool.center.y - 2f), new Vector3(0.2f, 1f, 8f), wood);
    }

    /// <summary>
    /// SE. The rock hill - the ground itself (see ShapeGround): a shelf 3m up you can jump onto from
    /// the plaza side, cliffs above it to the top 7m up, and a long dirt slope up the far side. A
    /// fence round the plaza-facing sides with gaps in it, trees and rocks on the slopes.
    /// </summary>
    static void BuildRockHill(Transform map)
    {
        Transform rocks = Group(map, "RockHill");
        Vector2 c = RockHill;

        Grown(rocks, "tree_tall_dark", c + new Vector2(2f, -2f), 20f, 7f);
        Grown(rocks, "tree_detailed_dark", c + new Vector2(-8f, -9f), 140f, 6f);
        Grown(rocks, "tree_palmDetailedTall", c + new Vector2(-6f, 5f), 200f, 6f);
        Grown(rocks, "rock_largeB", c + new Vector2(9f, 6f), 60f, 4f);
        Grown(rocks, "rock_tallB", c + new Vector2(-3f, 8f), 10f, 3f);
        Grown(rocks, "rock_largeA", c + new Vector2(-9f, -2f), 200f, 3.5f);
        Grown(rocks, "stone_tallA", c + new Vector2(4f, 3f), 90f, 3f);

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

        // A floor, so no lawn grows indoors.
        Slab(house, x0, x1, z0, z1, stone);

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

        // Nobody's cut the aviary back either.
        System.Random random = new System.Random(38);
        Rect inside = Rect.MinMaxRect(x0 + 1f, z0 + 1f, x1 - 1f, z1 - 1f);
        TerrainKit.Spread(aviary, ground, random, Undergrowth, 26, inside, null, null);
        TerrainKit.Spread(aviary, ground, random, Bushes, 6, inside, at => (at - inside.center).magnitude > 3f, taken);
    }

    // ---------------------------------------------------------------- the new ground (2026-09-30)

    /// East, where the avenue ends: a big barn the path runs straight through, doors at both ends,
    /// a keeper's walkway along the north wall to fight down from, hay bales for cover.
    static void BuildElephantHouse(Transform map)
    {
        Transform house = Group(map, "ElephantHouse");
        Material wood = kit["woodDark"];
        float x0 = 49f, x1 = 64f, z0 = -11f, z1 = 11f, height = 9f, t = 0.8f, door = 7f, doorHeight = 6.5f;
        float cx = (x0 + x1) * 0.5f;

        Slab(house, x0, x1, z0, z1, kit["stone"]);

        Block(house, "North", new Vector3(cx, height * 0.5f, z1), new Vector3(x1 - x0, height, t), wood);
        Block(house, "South", new Vector3(cx, height * 0.5f, z0), new Vector3(x1 - x0, height, t), wood);

        // The two ends, each a wall with a doorway on the avenue's line.
        foreach ((float x, string end) in new[] { (x0, "West"), (x1, "East") })
        {
            float side = (z1 - z0 - door) * 0.5f;
            Block(house, end + "Left", new Vector3(x, height * 0.5f, z0 + side * 0.5f), new Vector3(t, height, side), wood);
            Block(house, end + "Right", new Vector3(x, height * 0.5f, z1 - side * 0.5f), new Vector3(t, height, side), wood);
            Block(house, end + "Lintel", new Vector3(x, (height + doorHeight) * 0.5f, 0f), new Vector3(t, height - doorHeight, door), wood);
        }

        Block(house, "Roof", new Vector3(cx, height + 0.25f, 0f), new Vector3(x1 - x0 + t, 0.5f, z1 - z0 + t), kit["stoneDark"]);

        // The walkway, 3.5m up along the north wall, and a ramp up to it from the west door.
        Block(house, "Walkway", new Vector3(cx, 3.3f, 9f), new Vector3(x1 - x0 - 1f, 0.4f, 3.6f), kit["woodBark"]);
        Ramp(house, "WalkwayRamp", new Vector3(x0 + 2f, 0f, 5.6f), new Vector3(x0 + 10f, 3.5f, 5.6f), 2.8f, kit["woodBark"]);

        // Hay, and a trough.
        foreach (Vector2 at in new[] { new Vector2(54f, -7f), new Vector2(59f, -5f), new Vector2(61f, 3f), new Vector2(56f, 1f) })
            Block(house, "Hay", new Vector3(at.x, 0.7f, at.y), new Vector3(2.2f, 1.4f, 1.4f), kit["dirt"]);
        Block(house, "Trough", new Vector3(cx, 0.5f, -9.5f), new Vector3(8f, 1f, 1.2f), kit["stone"]);
    }

    /// West, either side of the avenue: tall palms and two feeding towers - a deck 6m up on posts,
    /// a long ramp to each - the high ground on that side.
    static void BuildGiraffePaddock(Transform map)
    {
        Transform paddock = Group(map, "GiraffePaddock");
        Material wood = kit["woodBark"];

        foreach (float z in new[] { 13f, -13f })
        {
            Transform tower = Group(paddock, z > 0 ? "TowerNorth" : "TowerSouth");
            float x = -58f, deck = 6f;

            foreach (Vector2 corner in new[] { new Vector2(-2f, -2f), new Vector2(2f, -2f), new Vector2(-2f, 2f), new Vector2(2f, 2f) })
                Block(tower, "Post", new Vector3(x + corner.x, deck * 0.5f, z + corner.y), new Vector3(0.4f, deck, 0.4f), wood);

            Block(tower, "Deck", new Vector3(x, deck, z), new Vector3(4.8f, 0.4f, 4.8f), kit["woodDark"]);
            Block(tower, "Rail", new Vector3(x - 2.3f, deck + 0.6f, z), new Vector3(0.2f, 1f, 4.8f), wood);
            Ramp(tower, "Ramp", new Vector3(x + 14f, 0f, z), new Vector3(x + 2.4f, deck + 0.2f, z), 2.6f, wood);
        }

        Random.State saved = Random.state;
        Random.InitState(20260930);
        foreach (Vector2 at in new[] { new Vector2(-52f, 22f), new Vector2(-63f, 6f), new Vector2(-50f, -22f), new Vector2(-63f, -6f), new Vector2(-54f, 30f), new Vector2(-54f, -30f) })
            Grown(paddock, "tree_palmTall", new Vector3(at.x, 0f, at.y), Random.Range(0f, 360f), Random.Range(7f, 9f));
        Random.state = saved;

        // A low fence along the avenue, with its gaps.
        Fence(paddock, new Vector3(-48f, 0f, 4f), new Vector3(-66f, 0f, 4f));
        Fence(paddock, new Vector3(-48f, 0f, -4f), new Vector3(-66f, 0f, -4f));
    }

    /// North, beside the avenue: a cafe with a counter, an open front, and tables under umbrellas
    /// out on its terrace.
    static void BuildCafe(Transform map)
    {
        Transform cafe = Group(map, "Cafe");
        Material stone = kit["stone"];
        float x0 = 14f, x1 = 34f, z0 = 54f, z1 = 64f, height = 5f, t = 0.6f;
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

        Slab(cafe, x0, x1, z0 - 8f, z1, stone);   // the building and its terrace

        Block(cafe, "Back", new Vector3(cx, height * 0.5f, z1), new Vector3(x1 - x0, height, t), stone);
        Block(cafe, "West", new Vector3(x0, height * 0.5f, cz), new Vector3(t, height, z1 - z0), stone);
        Block(cafe, "East", new Vector3(x1, height * 0.5f, cz), new Vector3(t, height, z1 - z0), stone);
        Block(cafe, "Roof", new Vector3(cx, height + 0.25f, cz), new Vector3(x1 - x0 + t, 0.5f, z1 - z0 + t), kit["woodDark"]);
        for (int i = 0; i <= 4; i++)
            Block(cafe, $"Pillar{i}", new Vector3(Mathf.Lerp(x0, x1, i / 4f), height * 0.5f, z0), new Vector3(0.6f, height, 0.6f), stone);

        Block(cafe, "Counter", new Vector3(cx, 0.55f, z1 - 3f), new Vector3(12f, 1.1f, 1.2f), kit["woodDark"]);

        foreach (Vector2 at in new[] { new Vector2(17f, 50f), new Vector2(23f, 49f), new Vector2(29f, 50f), new Vector2(20f, 58f), new Vector2(28f, 58f) })
        {
            Block(cafe, "Table", new Vector3(at.x, 0.45f, at.y), new Vector3(1.4f, 0.9f, 1.4f), kit["woodBark"]);
            if (at.y < z0)
            {
                Disc(cafe, "UmbrellaPole", new Vector3(at.x, 1.4f, at.y), 0.12f, 2.6f, kit["woodBark"]);
                Disc(cafe, "Umbrella", new Vector3(at.x, 2.8f, at.y), 3f, 0.15f, kit["stoneDark"]);
            }
        }
    }

    /// South, east of the avenue: a monkey climbing frame - posts and beams on a 4m grid, two
    /// levels of decks and a top the vine can catch, a ramp onto the first level.
    static void BuildMonkeyFrame(Transform map)
    {
        Transform frame = Group(map, "MonkeyFrame");
        Material wood = kit["woodBark"];
        float x0 = 16f, x1 = 36f, z0 = -62f, z1 = -50f, cell = 4f, top = 8f;

        for (float x = x0; x <= x1 + 0.01f; x += cell)
        {
            for (float z = z0; z <= z1 + 0.01f; z += cell)
                Block(frame, "Post", new Vector3(x, top * 0.5f, z), new Vector3(0.3f, top, 0.3f), wood);

            Block(frame, "BeamLow", new Vector3(x, 4f, (z0 + z1) * 0.5f), new Vector3(0.25f, 0.25f, z1 - z0), wood);
            Block(frame, "BeamTop", new Vector3(x, top, (z0 + z1) * 0.5f), new Vector3(0.25f, 0.25f, z1 - z0), wood);
        }

        for (float z = z0; z <= z1 + 0.01f; z += cell)
            Block(frame, "BeamTop", new Vector3((x0 + x1) * 0.5f, top, z), new Vector3(x1 - x0, 0.25f, 0.25f), wood);

        // Decks on some cells, at the two levels - somewhere to stand, not a floor all the way.
        foreach ((float x, float z, float y) in new[] { (x0 + 2f, z0 + 2f, 4f), (x0 + 6f, z0 + 2f, 4f), (x0 + 10f, z0 + 6f, 4f),
                                                         (x1 - 2f, z1 - 2f, 4f), (x0 + 6f, z0 + 6f, 7.8f), (x1 - 6f, z0 + 2f, 7.8f) })
            Block(frame, "Deck", new Vector3(x, y, z), new Vector3(3.8f, 0.3f, 3.8f), kit["woodDark"]);

        Ramp(frame, "Ramp", new Vector3(x0 - 8f, 0f, z0 + 2f), new Vector3(x0 + 0.1f, 4.15f, z0 + 2f), 2.5f, wood);
    }

    /// South, west of the avenue: a small keeper's hut - a door, window gaps, crates inside.
    static void BuildKeepersHut(Transform map)
    {
        Transform hut = Group(map, "KeepersHut");
        Material stone = kit["stoneDark"];
        float x0 = -38f, x1 = -26f, z0 = -62f, z1 = -52f, height = 4.5f, t = 0.5f;
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

        Slab(hut, x0, x1, z0, z1, kit["stone"]);

        // The door faces the middle of the map; the side walls have window gaps.
        Block(hut, "FrontLeft", new Vector3(x0 + 2.5f, height * 0.5f, z1), new Vector3(5f, height, t), stone);
        Block(hut, "FrontRight", new Vector3(x1 - 2.5f, height * 0.5f, z1), new Vector3(5f, height, t), stone);
        Block(hut, "FrontLintel", new Vector3(cx, height - 0.75f, z1), new Vector3(2f, 1.5f, t), stone);
        Block(hut, "Back", new Vector3(cx, height * 0.5f, z0), new Vector3(x1 - x0, height, t), stone);

        foreach (float x in new[] { x0, x1 })
        {
            Block(hut, "SideLow", new Vector3(x, 0.6f, cz), new Vector3(t, 1.2f, z1 - z0), stone);
            Block(hut, "SideHigh", new Vector3(x, height - 0.9f, cz), new Vector3(t, 1.8f, z1 - z0), stone);
            Block(hut, "SidePier", new Vector3(x, height * 0.5f, cz), new Vector3(t, height, 2f), stone);
        }

        Block(hut, "Roof", new Vector3(cx, height + 0.25f, cz), new Vector3(x1 - x0 + t, 0.5f, z1 - z0 + t), kit["woodDark"]);

        foreach (Vector2 at in new[] { new Vector2(-35f, -60f), new Vector2(-29f, -59f), new Vector2(-33f, -56f) })
            Block(hut, "Crate", new Vector3(at.x, 0.6f, at.y), new Vector3(1.2f, 1.2f, 1.2f), kit["woodBark"]);
    }

    /// The four outer corners: a water tower (NE) to climb or swing to, bamboo in a hollow (NW), a
    /// rocky knoll (SW) with a slope up its east side, and a wooded hill (SE).
    static void BuildCorners(Transform map)
    {
        System.Random random = new System.Random(20260931);

        // NE - the water tower: four legs, a deck halfway up with a ramp to it, the tank on top.
        Transform tower = Group(map, "WaterTower");
        Vector3 c = new Vector3(56f, 0f, 56f);
        foreach (Vector2 corner in new[] { new Vector2(-2.5f, -2.5f), new Vector2(2.5f, -2.5f), new Vector2(-2.5f, 2.5f), new Vector2(2.5f, 2.5f) })
            Block(tower, "Leg", c + new Vector3(corner.x, 6f, corner.y), new Vector3(0.5f, 12f, 0.5f), kit["woodBark"]);
        Block(tower, "Deck", c + new Vector3(0f, 6f, 0f), new Vector3(6f, 0.4f, 6f), kit["woodDark"]);
        Ramp(tower, "Ramp", c + new Vector3(-15f, 0f, 0f), c + new Vector3(-3f, 6.2f, 0f), 2.6f, kit["woodBark"]);
        Disc(tower, "Tank", c + new Vector3(0f, 14f, 0f), 7f, 4f, kit["stoneDark"]);

        // NW - bamboo in the hollow: a thicket to hide in and swing between.
        Transform bamboo = Group(map, "BambooHollow");
        TerrainKit.Spread(bamboo, ground, random, new TerrainKit.Scatter
        {
            folder = Models, models = new[] { "crops_bambooStageB", "crops_bambooStageA" },
            scale = new Vector2(4.5f, 7f), spacing = 1.6f, maxSteep = 0.35f,
        }, 45, new Rect(-66f, 46f, 20f, 20f), at => (at - new Vector2(-56f, 56f)).magnitude < 10f, taken);

        // SW - the rocky knoll: rocks round its foot and on its top.
        Transform knoll = Group(map, "RockyKnoll");
        foreach ((string model, Vector2 at, float size) in new[] { ("rock_largeA", new Vector2(-50f, -63f), 4.5f),
                                                                    ("rock_largeB", new Vector2(-63f, -49f), 4f),
                                                                    ("rock_tallA", new Vector2(-58f, -58f), 3.5f),
                                                                    ("stone_largeA", new Vector2(-55f, -60f), 3f),
                                                                    ("rock_tallB", new Vector2(-63f, -62f), 4f) })
            Grown(knoll, model, at, at.x * 13f, size);
        Grown(knoll, "tree_detailed_dark", new Vector2(-59f, -54f), 30f, 6f);

        // SE - the wooded hill.
        Transform copse = Group(map, "WoodedHill");
        TerrainKit.Spread(copse, ground, random, Trees, 11, new Rect(44f, -66f, 22f, 22f),
                          at => (at - new Vector2(56f, -56f)).magnitude < 10f, taken);
    }

    // ---------------------------------------------------------------- gone wild

    static readonly TerrainKit.Scatter Trees = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "tree_tall", "tree_default", "tree_detailed", "tree_tall_dark", "tree_detailed_dark",
                         "tree_default_dark", "tree_palmTall", "tree_palmDetailedTall", "tree_thin", "tree_thin_dark" },
        scale = new Vector2(4.5f, 8f), spacing = 6f, maxSteep = 0.3f,
    };

    static readonly TerrainKit.Scatter Bushes = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "plant_bush", "plant_bushLarge", "plant_bushDetailed", "plant_bushSmall" },
        scale = new Vector2(4f, 7f), solid = false, spacing = 2.6f, maxSteep = 0.35f,
    };

    static readonly TerrainKit.Scatter Undergrowth = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "plant_flatTall", "plant_flatShort", "grass_large", "grass_leafsLarge", "grass_leafs",
                         "flower_purpleA", "flower_redA", "flower_yellowA", "mushroom_redGroup", "mushroom_tanGroup" },
        scale = new Vector2(2.4f, 4.2f), solid = false, spacing = 0f, maxSteep = 0.35f, sink = 0.02f, shadows = false,
    };

    static readonly TerrainKit.Scatter Rocks = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "rock_largeA", "rock_largeB", "rock_tallA", "rock_smallA", "stone_largeA", "log", "log_large" },
        scale = new Vector2(3.5f, 6f), spacing = 5f, maxSteep = 0.4f, sink = 0.1f,
    };

    /// <summary>
    /// The zoo's been left to the jungle: trees, bushes, ferns, flowers and fallen logs across
    /// every lawn - never on a path, a building or a spawnpoint. Bushes and undergrowth have no
    /// collider, so they hide you without stopping you; trees, rocks and logs are solid cover.
    /// </summary>
    static int Overgrow(Transform map)
    {
        Transform wild = Group(map, "Overgrowth");
        System.Random random = new System.Random(20261001);
        Rect all = new Rect(-Half + 2f, -Half + 2f, Half * 2f - 4f, Half * 2f - 4f);

        bool Open(Vector2 at, float margin)
        {
            if (TerrainKit.InAny(at, Pads, margin))
                return false;

            // Nothing growing across the slopes up the rock hill and the knoll.
            foreach (Vector2[] way in Ways)
            {
                if (TerrainKit.DistanceToLine(at, way, out _) < 3f + margin)
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
        placed += TerrainKit.Spread(wild, ground, random, Trees, 70, all, at => Open(at, 3f), taken);
        placed += TerrainKit.Spread(wild, ground, random, Rocks, 18, all, at => Open(at, 2f), taken);
        placed += TerrainKit.Spread(wild, ground, random, Bushes, 130, all, at => Open(at, 1f), taken);
        placed += TerrainKit.Spread(wild, ground, random, Undergrowth, 340, all, at => Open(at, 0.5f), null);

        // Along the foot of the walls, thickest - the edge of the map reads as jungle, not a wall.
        foreach (Rect edge in new[] { Rect.MinMaxRect(-Half, Half - 5f, Half, Half), Rect.MinMaxRect(-Half, -Half, Half, -Half + 5f),
                                      Rect.MinMaxRect(Half - 5f, -Half, Half, Half), Rect.MinMaxRect(-Half, -Half, -Half + 5f, Half) })
        {
            placed += TerrainKit.Spread(wild, ground, random, Bushes, 25, edge, at => Open(at, 0.5f), taken);
            placed += TerrainKit.Spread(wild, ground, random, Undergrowth, 40, edge, at => Open(at, 0.5f), null);
        }

        return placed;
    }

    // The lobby camera's view (MapSetup.ViewSpots): from where it stands, past the gorilla, up the lawn.
    static readonly Vector2[] LobbyView = { new Vector2(-8.5f, -37.5f), new Vector2(-4f, -26f) };

    // Two per inner enclosure, one or two in each new area - never in the plaza or the pool.
    static readonly Vector2[] SpawnSpots =
    {
        new Vector2(16f, 40f), new Vector2(40f, 17f),      // pool
        new Vector2(18f, -40f), new Vector2(40f, -18f),    // rock hill
        new Vector2(-28f, -31f), new Vector2(-40f, -17f),  // reptile house, inside and out
        new Vector2(-30f, 23f), new Vector2(-16f, 43f),    // aviary, inside and out
        new Vector2(61f, -7.3f),                           // elephant house, between the hay and the trough
        new Vector2(-52f, 20f), new Vector2(-52f, -20f),   // giraffe paddock
        new Vector2(24f, 58f),                             // cafe
        new Vector2(12f, -57f),                            // by the monkey frame
        new Vector2(-32f, -57f),                           // keeper's hut
    };

    /// Facing the middle of the map, standing on whatever the ground is there.
    static void BuildSpawns()
    {
        GameObject host = new GameObject("SpawnManager");
        host.AddComponent<SpawnManager>();
        GameObject pad = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnpointPrefab);
        Vector2[] spots = SpawnSpots;

        for (int i = 0; i < spots.Length; i++)
        {
            Vector3 at = new Vector3(spots[i].x, ground.Height(spots[i].x, spots[i].y) + 1.1f, spots[i].y);
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

    /// A building's floor - a thin slab over its level pad, which is also what keeps the grass out.
    /// Not called "Floor": that's the terrain's name, and what the lobby's grass copy looks for.
    static void Slab(Transform parent, float x0, float x1, float z0, float z1, Material material)
    {
        Block(parent, "Slab", new Vector3((x0 + x1) * 0.5f, 0.01f, (z0 + z1) * 0.5f),
              new Vector3(x1 - x0, 0.1f, z1 - z0), material);
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

    /// Wooden fence rails between two points, leaving a 4m gap every 12m.
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
            mid.y = ground.Height(mid.x, mid.z);
            GameObject rail = Block(fence, "Rail", mid + Vector3.up * 0.7f, new Vector3(0.2f, 1.4f, segment), wood);
            rail.transform.localRotation = facing;
        }
    }

    /// One of the jungle kit's models, solid unless it's told not to be.
    static GameObject Prop(Transform parent, string model, Vector3 at, float yaw, float scale, bool solid = true)
    {
        return TerrainKit.Prop(parent, $"{Models}/{model}.fbx", at, yaw, scale, solid);
    }

    /// One standing on the ground wherever it is, a little sunk in so it doesn't perch on a slope.
    static GameObject Grown(Transform parent, string model, Vector2 at, float yaw, float scale, bool solid = true)
    {
        taken.Add(new Vector3(at.x, at.y, 3f));
        return Prop(parent, model, new Vector3(at.x, ground.Height(at.x, at.y) - 0.05f * scale, at.y), yaw, scale, solid);
    }

    static GameObject Grown(Transform parent, string model, Vector3 at, float yaw, float scale) =>
        Grown(parent, model, new Vector2(at.x, at.z), yaw, scale);

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
