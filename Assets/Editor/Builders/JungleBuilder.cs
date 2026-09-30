using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The jungle - the first map, `Scenes/Game.unity` - on terrain, and thick with it.
///
/// Reported 2026-09-30: "try using the terrain builder instead of shapes and boxes for everything
/// so you can add some verticality to the map. Right now, the blocky cliffs and rocks arent it and
/// theyre more so decoration", then "make it feel like a proper jungle yk" - for both maps. The
/// arena was a flat plane with four towers of stacked cliff blocks on it (MapExpansion, retired
/// with this). Now the ground is the shape:
/// - a dry creek winding north to south through the middle, a ravine you can run along or cross;
/// - the idol raised on a temple mesa in the east, cliffs round it and two slopes up;
/// - a ridge along the west with two rocky tops and a saddle between them;
/// - a knoll of fallen ruins in the north-east, low hills north and south-east, a hollow in the
///   south-west, and a clearing in the middle to fight across;
/// - the ground banking up into cliffs against the rock walls all round, so the edge of the map is
///   the valley's side, not a wall standing on a floor.
/// And it's overgrown: a hundred and fifty trees, bamboo, bushes to hide in, ferns and flowers
/// everywhere, logs and rocks for cover.
///
/// Replaces the scene's "Map" and its spawnpoints and keeps everything else - the sun, the sky,
/// the grass (pointed at the new ground), post-processing, the spawn manager itself - and from the
/// old map the rock walls and the idol, which were never the problem. Safe to run again: it only
/// ever keeps those two.
/// </summary>
public static class JungleBuilder
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string Models = "Assets/Art/Jungle/Models";
    const string FloorMaterialPath = "Assets/Art/Jungle/Materials/grass.mat";
    const string SpawnpointPrefab = "Assets/Prefabs/World/Spawnpoint.prefab";
    const string TerrainPath = "Assets/Scenes/Terrain/Jungle.asset";
    const string GroundMaterialPath = "Assets/Scenes/Terrain/JungleGround.mat";

    // The old floor plane's size - the walls stand just inside it.
    const float Size = 128f;
    const float Half = Size * 0.5f;

    // The idol's temple, east.
    static readonly Vector2 Temple = new Vector2(40f, 0f);
    const float TempleHeight = 5f;

    // The dry creek, north to south.
    static readonly Vector2[] Creek =
    {
        new Vector2(-26f, 68f), new Vector2(-18f, 36f), new Vector2(-4f, 16f), new Vector2(-8f, -6f),
        new Vector2(6f, -26f), new Vector2(4f, -68f),
    };

    // The slopes up onto high ground, kept clear of anything growing.
    static readonly Vector2[][] Ways =
    {
        new[] { new Vector2(23f, 8.5f), new Vector2(31.5f, 4f) },        // temple, from the west
        new[] { new Vector2(42f, -25f), new Vector2(41f, -12.5f) },      // temple, from the south
        new[] { new Vector2(-40f, -2f), new Vector2(-42f, -12f) },       // ridge, the south top
        new[] { new Vector2(-40f, -2f), new Vector2(-39.5f, 7.5f) },     // ridge, the north top
    };

    static readonly Vector2 Clearing = new Vector2(16f, 4f);

    // The lobby camera's view (MenuBuilder.PlaceCamera): from where it stands, past where the gorilla
    // stands, into the clearing - kept clear so the backdrop isn't a tree trunk.
    static readonly Vector2[] LobbyView = { new Vector2(6f, 1.5f), new Vector2(18f, 3.5f) };

    // Twelve round the edge of the fighting, none in the creek or on a slope, all facing in.
    static readonly Vector2[] SpawnSpots =
    {
        new Vector2(-8f, 54f), new Vector2(15f, 52f), new Vector2(46f, 40f), new Vector2(50f, 16f),
        new Vector2(50f, -24f), new Vector2(18f, -50f), new Vector2(-12f, -50f), new Vector2(-20f, -44f),
        new Vector2(-52f, 30f), new Vector2(-22f, 5f), new Vector2(18f, 22f), new Vector2(18f, -14f),
    };

    static HeightMap ground;
    static readonly List<Vector3> taken = new List<Vector3>();

    [MenuItem("Tools/Gorilla Warfare/Rebuild the jungle on terrain (replaces its Map)")]
    public static void Rebuild()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject oldMap = null;
        SpawnManager spawns = null;
        GrassField grass = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Map")
                oldMap = root;
            if (spawns == null)
                spawns = root.GetComponentInChildren<SpawnManager>(true);
            if (grass == null)
                grass = root.GetComponentInChildren<GrassField>(true);
        }

        Transform border = oldMap != null ? oldMap.transform.Find("Border") : null;
        Transform idol = oldMap != null ? oldMap.transform.Find("TheIdol") : null;
        Material grassMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        Dictionary<string, Material> kit = KitMaterials();

        if (border == null || idol == null || spawns == null || grassMaterial == null || !kit.ContainsKey("dirt") || !kit.ContainsKey("stoneDark"))
        {
            Fail($"expected the jungle's Map/Border, Map/TheIdol, a SpawnManager, {FloorMaterialPath} and the kit's dirt and stone - "
                 + $"found border {border != null}, idol {idol != null}, spawns {spawns != null}, grass {grassMaterial != null}");
            return;
        }

        Bounds walls = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer r in border.GetComponentsInChildren<Renderer>())
            walls.Encapsulate(r.bounds);

        // A new Map with the walls and the idol carried across, in the old one's place in the list.
        GameObject map = new GameObject("Map");
        SceneManager.MoveGameObjectToScene(map, scene);
        map.transform.SetSiblingIndex(oldMap.transform.GetSiblingIndex());
        border.SetParent(map.transform, true);
        idol.SetParent(map.transform, true);
        Object.DestroyImmediate(oldMap);
        taken.Clear();

        // The kit's own colours, the same as the zoo's: the arena floor's grass on the level, its
        // dirt on a slope you can still climb, its dark stone on a cliff you can't. (Not the walls'
        // colour - the cliff pieces they're made of are dirt with grass on top, and a dirt cliff
        // reads as just more slope.)
        ground = ShapeGround();
        Material groundMaterial = TerrainKit.GroundMaterial(GroundMaterialPath, grassMaterial.color, kit["dirt"].color, kit["stoneDark"].color);
        Terrain terrain = TerrainKit.Build(ground, map.transform, TerrainPath, groundMaterial);
        terrain.transform.SetAsFirstSibling();

        // Up onto the temple, the idol centred on its top.
        Vector3 idolAt = idol.position;
        idol.position = new Vector3(idolAt.x, TempleHeight, idolAt.z);

        int wild = Overgrow(map.transform);
        BuildRuins(map.transform);
        PlaceSpawns(spawns);

        if (grass != null)
        {
            SerializedObject so = new SerializedObject(grass);
            SerializedProperty list = so.FindProperty("ground");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = terrain.GetComponent<TerrainCollider>();
            so.ApplyModifiedPropertiesWithoutUndo();
            TerrainKit.GrassOnFlatOnly(grass);
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[jungle] rebuilt {ScenePath} on terrain - walls span {walls.min:F1} to {walls.max:F1}, "
                  + $"{map.GetComponentsInChildren<Renderer>().Length} pieces, {wild} growing wild, {SpawnSpots.Length} spawnpoints");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    // ---------------------------------------------------------------- the ground

    static HeightMap ShapeGround()
    {
        HeightMap g = new HeightMap(Size, 513, -8f, 36f);

        g.Noise(1.4f, 14f, 11);

        g.Trench(Creek, 5f, 6f, 3.5f);

        // The temple: a flat top with the idol on it, cliffs, and the two slopes up.
        g.Mesa(Temple, 11f, TempleHeight, 2.2f, 0.12f, 3);
        g.Ramp(Ways[0][0], 0f, Ways[0][1], TempleHeight, 4f, 2f);
        g.Ramp(Ways[1][0], 0f, Ways[1][1], TempleHeight, 4f, 2f);

        // The ridge in the west: a long rise, two rocky tops, slopes up to each from the saddle.
        g.Ridge(new Vector2(-44f, -32f), new Vector2(-38f, 22f), 11f, 6f);
        g.Mesa(new Vector2(-43f, -18f), 6f, 9.5f, 2.5f, 0.2f, 6);
        g.Mesa(new Vector2(-39f, 12f), 5f, 9f, 2.5f, 0.2f, 8);
        g.Ramp(Ways[2][0], 6f, Ways[2][1], 9.5f, 3.5f, 1.5f);
        g.Ramp(Ways[3][0], 6f, Ways[3][1], 9f, 3.5f, 1.5f);

        g.Hill(new Vector2(30f, 40f), 13f, 4.5f);                   // NE, the ruins' knoll
        g.Hill(new Vector2(36f, -40f), 12f, 3.5f);                  // SE
        g.Mesa(new Vector2(26f, -47f), 4f, 4.5f, 1.4f, 0.25f, 12);  // and a rock to jump up
        g.Disc(new Vector2(-30f, -40f), 5f, -2.2f, 8f);             // SW, the hollow
        g.Hill(new Vector2(2f, 44f), 10f, 2.5f);                    // N
        g.Hill(new Vector2(-42f, 46f), 10f, 3f);

        g.Rim(46f, 12f, 4f);

        g.Disc(Clearing, 7f, 0f, 5f);

        // Every spawnpoint on a little level ground - nobody comes back on a slope, and there's a
        // flat few metres round you to fight from.
        foreach (Vector2 spot in SpawnSpots)
            g.Disc(spot, 3f, g.Height(spot.x, spot.y), 5f);

        return g;
    }

    // ---------------------------------------------------------------- the jungle

    static readonly TerrainKit.Scatter Trees = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "tree_tall", "tree_tall_dark", "tree_default", "tree_default_dark", "tree_detailed",
                         "tree_detailed_dark", "tree_thin", "tree_thin_dark", "tree_palmTall", "tree_palmDetailedTall",
                         "tree_palmShort", "tree_palmDetailedShort" },
        scale = new Vector2(5f, 9f), spacing = 5.5f, maxSteep = 0.3f,
    };

    static readonly TerrainKit.Scatter Bushes = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "plant_bush", "plant_bushLarge", "plant_bushDetailed", "plant_bushSmall" },
        scale = new Vector2(4f, 7.5f), solid = false, spacing = 2.4f, maxSteep = 0.4f,
    };

    static readonly TerrainKit.Scatter Undergrowth = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "plant_flatTall", "plant_flatShort", "grass_large", "grass_leafsLarge", "grass_leafs", "grass",
                         "flower_purpleA", "flower_redA", "flower_yellowA", "mushroom_red", "mushroom_redGroup", "mushroom_tanGroup" },
        scale = new Vector2(2.4f, 4.5f), solid = false, spacing = 0f, maxSteep = 0.4f, sink = 0.02f, shadows = false,
    };

    static readonly TerrainKit.Scatter Cover = new TerrainKit.Scatter
    {
        folder = Models,
        models = new[] { "rock_largeA", "rock_largeB", "rock_tallA", "rock_tallB", "stone_largeA", "stone_tallA",
                         "log", "log_large", "log_stack" },
        scale = new Vector2(3.5f, 6f), spacing = 5f, maxSteep = 0.45f, sink = 0.1f,
    };

    static readonly TerrainKit.Scatter Bamboo = new TerrainKit.Scatter
    {
        folder = Models, models = new[] { "crops_bambooStageB", "crops_bambooStageA" },
        scale = new Vector2(5f, 8f), spacing = 1.5f, maxSteep = 0.35f,
    };

    /// Everything that grows, never on a slope up, a spawnpoint, the temple's top or the clearing -
    /// and no trees in the creek bed, so it stays a way through.
    static int Overgrow(Transform map)
    {
        Transform wild = new GameObject("Jungle").transform;
        wild.SetParent(map, false);
        System.Random random = new System.Random(20260930);
        Rect all = new Rect(-Half + 4f, -Half + 4f, Size - 8f, Size - 8f);

        bool Open(Vector2 at, float margin, bool inCreek)
        {
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

            if ((at - Temple).magnitude < 8f + margin || (at - Clearing).magnitude < 5f + margin)
                return false;

            return inCreek || TerrainKit.DistanceToLine(at, Creek, out _) > 4f;
        }

        int placed = 0;

        // Bamboo first, in three thickets - west of the creek, the ridge's saddle, the south.
        foreach ((Vector2 centre, float radius) in new[] { (new Vector2(-28f, 22f), 7f), (new Vector2(-50f, -4f), 5f), (new Vector2(-4f, -40f), 6f) })
            placed += TerrainKit.Spread(wild, ground, random, Bamboo, 30, new Rect(centre - Vector2.one * radius, Vector2.one * radius * 2f),
                                        at => (at - centre).magnitude < radius && Open(at, 0f, false), taken);

        placed += TerrainKit.Spread(wild, ground, random, Trees, 150, all, at => Open(at, 2f, false), taken);
        placed += TerrainKit.Spread(wild, ground, random, Cover, 30, all, at => Open(at, 1.5f, true), taken);
        placed += TerrainKit.Spread(wild, ground, random, Bushes, 220, all, at => Open(at, 0.5f, false), taken);
        placed += TerrainKit.Spread(wild, ground, random, Undergrowth, 650, all, at => Open(at, 0f, true), null);

        return placed;
    }

    /// The north-east knoll: an older temple fallen down - carved blocks lying about its top and a
    /// head on its side, the idol's twin. Cover up on a hill.
    static void BuildRuins(Transform map)
    {
        Transform ruins = new GameObject("Ruins").transform;
        ruins.SetParent(map, false);
        Vector2 top = new Vector2(30f, 40f);

        foreach ((Vector2 offset, float size, float yaw) in new[] { (new Vector2(-4f, 2f), 8f, 12f), (new Vector2(3f, 4f), 6f, 40f),
                                                                     (new Vector2(5f, -3f), 9f, 75f), (new Vector2(-2f, -5f), 5f, 20f),
                                                                     (new Vector2(-6.5f, -2f), 6f, 60f), (new Vector2(-2.5f, 2.5f), 5f, 5f) })
        {
            Vector2 at = top + offset;
            TerrainKit.Prop(ruins, $"{Models}/statue_block.fbx", new Vector3(at.x, ground.Height(at.x, at.y) - 0.3f, at.y), yaw, size);
            taken.Add(new Vector3(at.x, at.y, 4f));
        }

        // A second block stacked on the first, and the fallen head.
        Vector2 stack = top + new Vector2(-4f, 2f);
        TerrainKit.Prop(ruins, $"{Models}/statue_block.fbx", new Vector3(stack.x, ground.Height(stack.x, stack.y) - 0.3f + 3.2f, stack.y), 30f, 6f);

        Vector2 head = top + new Vector2(1f, 0f);
        GameObject fallen = TerrainKit.Prop(ruins, $"{Models}/statue_head.fbx", new Vector3(head.x, ground.Height(head.x, head.y) + 1.2f, head.y), 0f, 6f);
        if (fallen != null)
            fallen.transform.rotation = Quaternion.Euler(0f, 130f, 90f);
    }

    /// The old pads out, twelve new ones in, each on whatever ground is there, facing the middle.
    static void PlaceSpawns(SpawnManager spawns)
    {
        foreach (Spawnpoint old in spawns.GetComponentsInChildren<Spawnpoint>(true))
            Object.DestroyImmediate(old.gameObject);

        GameObject pad = AssetDatabase.LoadAssetAtPath<GameObject>(SpawnpointPrefab);
        for (int i = 0; i < SpawnSpots.Length; i++)
        {
            Vector2 spot = SpawnSpots[i];
            Vector3 at = new Vector3(spot.x, ground.Height(spot.x, spot.y) + 1.1f, spot.y);
            GameObject point = (GameObject)PrefabUtility.InstantiatePrefab(pad, spawns.gameObject.scene);
            point.name = $"Spawnpoint{i}";
            point.transform.SetParent(spawns.transform, false);
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


    static void Fail(string why)
    {
        Debug.LogError("[jungle] " + why);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
