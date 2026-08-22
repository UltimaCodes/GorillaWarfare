using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Adds verticality and density to the jungle map without touching its existing footprint,
// walls or spawn points.
//
// Climbable cliff clusters - real elevated ground built from the Kenney cliff kit, tall enough
// to reward the movement tech built the same day (a jump alone gets partway, vault or a wall-run
// off the rock face gets the rest, the vine gets there in one) - plus a denser scatter of the
// existing prop kit for detail on the ground between them.
//
// Re-runnable: everything this places lives under one "~MapExpansion" group, deleted and rebuilt
// from scratch every run rather than accumulating - the same convention MapDressing already
// established for exactly this reason. Every piece gets an explicit MeshCollider, matching how
// the map's existing decoration already works (confirmed by DecalMeshCheck's investigation into
// the bullet-impact report) rather than relying on FBX import settings this script doesn't own.
public static class MapExpansion
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string Art = "Assets/Art/Jungle";
    const string GroupName = "~MapExpansion";

    // cliff_block_rock measures 1x1x1 at scale 1, pivot at the base - real numbers from
    // PropMeasure (Tools > Gorilla Warfare > Measure jungle props, since removed), not guessed.
    const float CliffUnitSize = 1f;

    [MenuItem("Tools/Gorilla Warfare/Expand the jungle map")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Physics.CheckSphere against colliders already in the scene placed 0 of 90 detail props
        // on the first attempt, which reads as "everything is blocked" rather than "nothing is".
        // A scene that was just opened hasn't necessarily synced its collider transforms into the
        // physics world yet; this forces it before any query runs, the standard fix for physics
        // queries reading stale straight after a scene load or a fresh instantiate.
        Physics.SyncTransforms();

        GameObject existing = GameObject.Find(GroupName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        GameObject group = new GameObject(GroupName);

        Bounds floor = FindFloorBounds(scene);
        List<Vector3> keepOut = FindKeepOutPoints(scene);

        Debug.Log($"[expand] floor bounds centre {floor.center} size {floor.size}, "
                  + $"{keepOut.Count} spawn points to avoid");

        int cliffPieces = BuildCliffClusters(group.transform, floor, keepOut);
        int detail = ScatterDetail(group.transform, floor, keepOut);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[expand] {cliffPieces} cliff pieces across the clusters, {detail} extra detail props");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static Bounds FindFloorBounds(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Floor"))
                    continue;

                // The Floor object itself typically carries only a Transform - its mesh sits on
                // a child, the same reason MapDressing.Paint() searches children rather than the
                // named object directly. Encapsulate every renderer found under it rather than
                // trusting there's exactly one.
                Bounds found = default;
                bool started = false;

                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (!started) { found = r.bounds; started = true; }
                    else found.Encapsulate(r.bounds);
                }

                if (started)
                    return found;
            }
        }

        Debug.LogWarning("[expand] no Floor renderer found - falling back to a guessed footprint");
        return new Bounds(Vector3.zero, new Vector3(60f, 1f, 60f));
    }

    static List<Vector3> FindKeepOutPoints(Scene scene)
    {
        List<Vector3> points = new List<Vector3>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Spawnpoint sp in root.GetComponentsInChildren<Spawnpoint>(true))
                points.Add(sp.transform.position);
        }

        return points;
    }

    static bool TooCloseToSpawn(Vector3 point, List<Vector3> keepOut, float radius)
    {
        foreach (Vector3 p in keepOut)
        {
            Vector3 flat = point - p;
            flat.y = 0f;

            if (flat.sqrMagnitude < radius * radius)
                return true;
        }

        return false;
    }

    static GameObject LoadProp(string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{name}.fbx");

        if (prefab == null)
            Debug.LogWarning($"[expand] no prop found at {Art}/{name}.fbx");

        return prefab;
    }

    static GameObject PlaceProp(string name, Transform parent, Vector3 position, float yaw, float scale)
    {
        GameObject prefab = LoadProp(name);
        if (prefab == null)
            return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        instance.transform.SetParent(parent, true);
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        instance.transform.localScale = Vector3.one * scale;

        AddMeshColliders(instance);
        return instance;
    }

    /// Explicit MeshColliders on every renderer, non-convex - matches how the map's existing
    /// decoration is set up. Not relying on FBX "Generate Colliders" import settings, since this
    /// script doesn't own those and shouldn't have to touch a shared import asset to place things.
    static void AddMeshColliders(GameObject instance)
    {
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                continue;

            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            collider.convex = false;
        }
    }

    /// <summary>
    /// Four rough cliff formations, one per quadrant of the floor, staggered up to five tiers.
    /// Each tier is a scaled cliff_block_rock (a real 1m cube at scale 1) offset slightly from
    /// the one below rather than stacked dead-centre, so it reads as a rock formation instead of
    /// a tower of crates - capped with a cliff_top_rock for a flat, standable summit, and given a
    /// cliff_blockSlope_rock ramp up one face so there is always an easy way up alongside the
    /// harder, faster one (wall-run or vault straight up the blunt faces).
    /// </summary>
    static int BuildCliffClusters(Transform parent, Bounds floor, List<Vector3> keepOut)
    {
        Vector3 half = floor.extents * 0.55f;
        Vector3[] quadrants =
        {
            floor.center + new Vector3(half.x, 0f, half.z),
            floor.center + new Vector3(-half.x, 0f, half.z),
            floor.center + new Vector3(half.x, 0f, -half.z),
            floor.center + new Vector3(-half.x, 0f, -half.z),
        };

        int placed = 0;

        foreach (Vector3 quadrant in quadrants)
        {
            if (TooCloseToSpawn(quadrant, keepOut, 7f))
                continue;

            GameObject cluster = new GameObject("~cliffCluster");
            cluster.transform.SetParent(parent, false);

            Vector3 basePos = new Vector3(quadrant.x, floor.min.y, quadrant.z);
            float baseYaw = Random.Range(0f, 360f);

            int tiers = Random.Range(4, 6);
            float y = floor.min.y;
            float tierSize = 2.6f;

            for (int i = 0; i < tiers; i++)
            {
                // Each tier drifts a little off the one below rather than stacking perfectly, and
                // shrinks slightly toward the top - a natural-looking outcrop rather than a
                // extruded column.
                float drift = i == 0 ? 0f : 0.6f;
                Vector3 offset = new Vector3(
                    Random.Range(-drift, drift), 0f, Random.Range(-drift, drift));

                float scale = tierSize * Mathf.Lerp(1f, 0.75f, i / (float)Mathf.Max(1, tiers - 1));

                // The bug the first render caught: this used to place every tier at `basePos`
                // itself (ground level) and only ever accumulate `y` without placing anything
                // there - so "stacked" tiers actually all sat on the ground at once, which is
                // exactly the scattered-debris pile the aerial shot showed instead of a tower.
                Vector3 tierPos = new Vector3(basePos.x + offset.x, y, basePos.z + offset.z);

                PlaceProp("cliff_block_rock", cluster.transform, tierPos,
                         baseYaw + Random.Range(-15f, 15f), scale);
                placed++;

                // A little overlap rather than edge-to-edge, so each tier is buried slightly into
                // the one below it instead of balanced on a seam.
                y += scale * CliffUnitSize * 0.82f;
            }

            // The summit - flat, so landing on it (by whatever means) is a real place to stand
            // and fight from, not a lumpy rock top.
            PlaceProp("cliff_top_rock", cluster.transform,
                     new Vector3(basePos.x, y, basePos.z), baseYaw, tierSize * 0.8f);
            placed++;

            // An easy way up on one side - a slope ramp leant against the lowest tier.
            Vector3 rampDir = Quaternion.Euler(0f, baseYaw + 90f, 0f) * Vector3.forward;
            Vector3 rampPos = basePos + rampDir * (tierSize * 0.6f);
            GameObject ramp = PlaceProp("cliff_blockSlope_rock", cluster.transform, rampPos,
                                       baseYaw + 90f, tierSize * 0.9f);
            if (ramp != null)
                ramp.transform.Rotate(Vector3.right, 25f, Space.Self);
            placed++;

            // A little ground scatter at the foot of the cliff, so it reads as part of the
            // jungle rather than a level designer's block-out left visible.
            for (int i = 0; i < 3; i++)
            {
                Vector3 at = basePos + Random.insideUnitSphere * (tierSize * 1.4f);
                at.y = floor.min.y;

                string prop = Random.value > 0.5f ? "plant_bush" : "rock_smallA";
                PlaceProp(prop, cluster.transform, at, Random.Range(0f, 360f), Random.Range(2f, 4f));
                placed++;
            }
        }

        return placed;
    }

    static readonly string[] DetailProps =
    {
        "tree_palmShort", "tree_palmTall", "plant_bush", "plant_bushLarge", "grass_large",
        "flower_redA", "flower_yellowA", "mushroom_tanGroup", "log", "stone_smallA",
    };

    /// <summary>
    /// A denser scatter of the existing prop kit across the open ground, on top of what's
    /// already hand-placed rather than replacing it - avoids spawn points and the cliff clusters
    /// just built, and skips a spot entirely if something is already there (Physics.CheckSphere
    /// against what's already in the scene, including the hand-placed decoration from before this
    /// script ever ran) rather than risk two props buried in each other.
    /// </summary>
    static int ScatterDetail(Transform parent, Bounds floor, List<Vector3> keepOut)
    {
        int placed = 0;
        int attempts = 90;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 at = new Vector3(
                Random.Range(floor.min.x, floor.max.x),
                floor.min.y,
                Random.Range(floor.min.z, floor.max.z));

            if (TooCloseToSpawn(at, keepOut, 3.5f))
                continue;

            // Centred and sized to stay clear of the floor itself - a sphere that dips down to
            // ground level always finds the floor's own collider and reports every point as
            // occupied, which is exactly what happened the first time this ran (0 of 90 placed).
            if (Physics.CheckSphere(at + Vector3.up * 1f, 0.55f, ~0, QueryTriggerInteraction.Ignore))
                continue;

            string prop = DetailProps[Random.Range(0, DetailProps.Length)];
            float scale = prop.StartsWith("tree") ? Random.Range(3f, 6f)
                        : prop.StartsWith("grass") || prop.StartsWith("flower") ? Random.Range(1.5f, 3f)
                        : Random.Range(1.5f, 3.5f);

            PlaceProp(prop, parent, at, Random.Range(0f, 360f), scale);
            placed++;
        }

        return placed;
    }
}
