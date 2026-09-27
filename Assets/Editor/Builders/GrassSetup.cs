using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts MinionsArt's grass system (Assets/Grass) into the game scene: its settings and material,
/// and a "Grass" object carrying GrassComputeScript plus GrassField, pointed at the floor.
///
/// Safe to re-run - it updates what's there rather than stacking a second copy. The numbers it
/// writes into the settings are the ones this project actually uses; the tool window
/// (Tools > Grass Tool > General Settings) edits the same asset afterwards.
/// </summary>
public static class GrassSetup
{
    const string ScenePath = "Assets/Scenes/Game.unity";
    const string Folder = "Assets/Grass/Settings";
    const string SettingsPath = Folder + "/GrassSettings.asset";
    const string MaterialPath = Folder + "/Grass.mat";
    const string ComputePath = "Assets/Grass/Shaders/GrassBlades.compute";
    const string ShaderName = "Custom/GrassComputeSurface";

    [MenuItem("Tools/Gorilla Warfare/Set up the grass")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Grass", "Settings");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform floor = FindFloor(scene);
        if (floor == null)
        {
            Debug.LogError("[grass] no object named Floor in the Game scene - nothing to grow grass on");
            Finish(1);
            return;
        }

        List<Collider> ground = new List<Collider>(floor.GetComponentsInChildren<Collider>(true));
        if (ground.Count == 0)
        {
            Debug.LogError("[grass] the Floor has no colliders - GrassField finds the ground by raycasting onto them");
            Finish(1);
            return;
        }

        Color floorColour = FloorColour(floor);
        Material material = BuildMaterial();
        SO_GrassSettings settings = BuildSettings(material, floorColour);

        GameObject holder = FindOrCreate(scene, "Grass");

        GrassComputeScript compute = holder.GetComponent<GrassComputeScript>();
        if (compute == null)
            compute = holder.AddComponent<GrassComputeScript>();
        compute.currentPresets = settings;

        GrassField field = holder.GetComponent<GrassField>();
        if (field == null)
            field = holder.AddComponent<GrassField>();

        // Back to the script's own defaults every run, so GrassField.cs stays the one place the
        // tuning lives - otherwise the scene keeps whatever the defaults were the day the component
        // was first added, and changing them in code does nothing (working-notes.md's first trap).
        GameObject scratch = new GameObject("~grass defaults");
        EditorUtility.CopySerialized(scratch.AddComponent<GrassField>(), field);
        Object.DestroyImmediate(scratch);

        SerializedObject so = new SerializedObject(field);
        SerializedProperty list = so.FindProperty("ground");
        list.arraySize = ground.Count;
        for (int i = 0; i < ground.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = ground[i];
        so.FindProperty("area").rectValue = ArenaInsideWalls(scene);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(compute);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Report(ground, floorColour, settings, field);
        Finish(0);
    }

    static Material BuildMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find(ShaderName);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        // The shader's own defaults are Metallic 1 and Specular 1 - chrome grass. Matte, like the
        // rest of the map. The ambient-adjustment colour multiplies the top tint, and at its default
        // grey it halved it, so the tint in the settings wasn't the colour you got.
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.12f);
        material.SetColor("_AmbientAdjustmentColor", Color.white);

        // The root-to-tip gradient is saturate((height + _Fade) * _Stretch); the default _Fade of 1
        // pins it at 1 for every height, so every blade was flat top-tint with no darker root.
        material.SetFloat("_Fade", 0f);
        material.SetFloat("_Stretch", 1f);
        EditorUtility.SetDirty(material);

        return material;
    }

    static SO_GrassSettings BuildSettings(Material material, Color floorColour)
    {
        SO_GrassSettings settings = AssetDatabase.LoadAssetAtPath<SO_GrassSettings>(SettingsPath);

        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<SO_GrassSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }

        settings.shaderToUse = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
        settings.materialToUse = material;

        // Heights and widths come per point from GrassField; these only clamp them, so they're
        // wide enough never to be what decides a blade's size.
        settings.MinHeight = 0.05f;
        settings.MaxHeight = 1f;
        settings.MinWidth = 0.01f;
        settings.MaxWidth = 1f;
        settings.grassRandomHeightMin = 0f;
        settings.grassRandomHeightMax = 0f;

        settings.allowedBladesPerVertex = 4;
        // Three, not the default four - the PSX filter renders at quarter resolution, and a fourth
        // bend per blade is a detail nobody can see there.
        settings.allowedSegmentsPerBlade = 3;
        settings.bladeRadius = 0.18f;
        settings.bladeForwardAmount = 0.35f;
        settings.bladeCurveAmount = 2f;
        settings.bottomWidth = 0.1f;

        settings.windSpeed = 6f;
        settings.windStrength = 0.04f;
        settings.affectStrength = 1f;

        // Rooted in the floor's own colour so the base of every blade disappears into the ground,
        // lighter and a touch warmer at the tips.
        settings.bottomTint = floorColour * 0.85f;
        settings.bottomTint.a = 1f;
        settings.topTint = Color.Lerp(floorColour, new Color(0.86f, 0.95f, 0.45f), 0.35f) * 1.1f;
        settings.topTint.a = 1f;

        settings.minFadeDistance = 35f;
        settings.maxDrawDistance = 60f;
        settings.cullingTreeDepth = 4;
        settings.castShadow = UnityEngine.Rendering.ShadowCastingMode.Off;

        EditorUtility.SetDirty(settings);
        return settings;
    }

    static Color FloorColour(Transform floor)
    {
        foreach (Renderer r in floor.GetComponentsInChildren<Renderer>(true))
        {
            Material m = r.sharedMaterial;
            if (m == null)
                continue;

            if (m.HasProperty("_Color"))
                return m.GetColor("_Color");

            if (m.HasProperty("_BaseColor"))
                return m.GetColor("_BaseColor");
        }

        return new Color(0.3f, 0.65f, 0.25f);
    }

    /// <summary>
    /// The XZ footprint the arena's walls enclose - the floor plane runs far past them. Every
    /// wall piece is named Wall-something; their combined bounds are the arena's outer edge.
    /// Empty (whole floor) if there are none.
    /// </summary>
    static Rect ArenaInsideWalls(Scene scene)
    {
        Bounds walls = default;
        bool any = false;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Wall"))
                    continue;

                if (!any) { walls = r.bounds; any = true; }
                else walls.Encapsulate(r.bounds);
            }
        }

        return any ? Rect.MinMaxRect(walls.min.x, walls.min.z, walls.max.x, walls.max.z) : default;
    }

    static Transform FindFloor(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("Floor"))
                    return t;
            }
        }

        return null;
    }

    static GameObject FindOrCreate(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }

        GameObject created = new GameObject(name);
        SceneManager.MoveGameObjectToScene(created, scene);
        return created;
    }

    static void Report(List<Collider> ground, Color floorColour, SO_GrassSettings settings, GrassField field)
    {
        Bounds area = ground[0].bounds;
        foreach (Collider c in ground)
            area.Encapsulate(c.bounds);

        SerializedObject so = new SerializedObject(field);
        float density = so.FindProperty("pointsPerSquareMetre").floatValue;
        int maxPoints = so.FindProperty("maxPoints").intValue;
        Rect arena = so.FindProperty("area").rectValue;

        float growArea = arena.width > 0f ? arena.width * arena.height : area.size.x * area.size.z;
        int estimate = Mathf.Min(maxPoints, Mathf.CeilToInt(growArea * density));

        Debug.Log($"[grass] arena inside the walls: {arena.width:F0} x {arena.height:F0} m at ({arena.center.x:F0}, {arena.center.y:F0}), "
                  + $"{growArea * density:F0} points wanted at {density}/m2, cap {maxPoints}");
        int trianglesPerPoint = settings.allowedBladesPerVertex * ((settings.allowedSegmentsPerBlade - 1) * 2 + 1);
        long bufferBytes = (long)estimate * trianglesPerPoint * sizeof(float) * (3 + 3 + (3 + 2) * 3);

        foreach (Collider c in ground)
            Debug.Log($"[grass] ground collider {c.name} ({c.GetType().Name}) on layer {LayerMask.LayerToName(c.gameObject.layer)}, bounds {c.bounds.size}");

        Debug.Log($"[grass] floor area {area.size.x:F0} x {area.size.z:F0} m, colour #{ColorUtility.ToHtmlStringRGB(floorColour)}; "
                  + $"up to {estimate} points x {trianglesPerPoint} triangles, draw buffer at most {bufferBytes / (1024f * 1024f):F0} MB");
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
