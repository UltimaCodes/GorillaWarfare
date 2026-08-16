using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Sets up the sourced art so it is cheap before anybody builds a map out of it.
//
// Two things matter here and neither is visible until it is too late. Import settings decide how
// much memory a mesh takes and whether it can be batched at all; materials decide how many draw
// calls a few hundred props turn into. Getting both right up front is the difference between a
// jungle and a slideshow.
//
// The Kenney nature models carry no textures - they are flat shaded and coloured entirely by
// material name, and the whole kit uses 23 names between 329 models. So this builds one shared
// material per name and points every renderer at it. A map of six hundred props then draws in
// about ten batches instead of six hundred.
public static class JungleImport
{
    const string Art = "Assets/Art/Jungle";
    const string Materials = Art + "/Materials";
    const string WeaponModels = "Assets/Resources/Models/Weapons";

    /// <summary>
    /// The palette, taken from the kit's own .mtl files rather than invented.
    ///
    /// Kenney ships these as Kd values on named materials; Unity's FBX importer does not carry
    /// them across, so they would otherwise all arrive white. Copied here so the models look the
    /// way the artist made them.
    /// </summary>
    static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        { "leafsGreen", new Color(0.161f, 0.788f, 0.671f) },
        { "leafsDark", new Color(0.106f, 0.545f, 0.463f) },
        { "leafsFall", new Color(0.925f, 0.549f, 0.192f) },
        { "woodBark", new Color(0.886f, 0.514f, 0.341f) },
        { "woodBarkDark", new Color(0.639f, 0.365f, 0.243f) },
        { "woodBirch", new Color(0.929f, 0.882f, 0.804f) },
        { "wood", new Color(0.769f, 0.545f, 0.373f) },
        { "woodDark", new Color(0.545f, 0.376f, 0.251f) },
        { "woodInner", new Color(0.965f, 0.784f, 0.588f) },
        { "grass", new Color(0.310f, 0.769f, 0.271f) },
        { "dirt", new Color(0.706f, 0.514f, 0.353f) },
        { "dirtDark", new Color(0.510f, 0.369f, 0.255f) },
        { "stone", new Color(0.643f, 0.659f, 0.678f) },
        { "stoneDark", new Color(0.451f, 0.463f, 0.478f) },
        { "water", new Color(0.259f, 0.647f, 0.961f) },
        { "_defaultMat", new Color(0.8f, 0.8f, 0.8f) },
    };

    [MenuItem("Tools/Gorilla Warfare/Set up the jungle art")]
    public static void Run()
    {
        Directory.CreateDirectory(Materials);

        int meshes = Tighten(Art) + Tighten(WeaponModels);
        int made = BuildMaterials();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[jungle] {meshes} meshes given cheap import settings, {made} shared materials");

        Report();

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// Import settings that keep a few hundred props affordable.
    ///
    /// Read/write off is the big one - leaving it on keeps a second copy of every mesh in system
    /// memory for no reason, since nothing here is read at runtime. Rigs and animation off
    /// because these are props and an Animator on each would be pure overhead. Static batching
    /// needs the meshes to be marked accordingly, which is what optimizeMeshForGPU does for us.
    /// </summary>
    static int Tighten(string folder)
    {
        int touched = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;

            if (importer == null)
                continue;

            bool changed = false;

            if (importer.isReadable) { importer.isReadable = false; changed = true; }
            if (importer.importAnimation) { importer.importAnimation = false; changed = true; }
            if (importer.animationType != ModelImporterAnimationType.None)
            {
                importer.animationType = ModelImporterAnimationType.None;
                changed = true;
            }

            if (importer.importCameras) { importer.importCameras = false; changed = true; }
            if (importer.importLights) { importer.importLights = false; changed = true; }

            // Off, deliberately. Compression quantises positions, and on models this small the
            // artefacts show up as visible wobble on trunks for a saving measured in kilobytes.
            if (importer.meshCompression != ModelImporterMeshCompression.Off)
            {
                importer.meshCompression = ModelImporterMeshCompression.Off;
                changed = true;
            }

            if (!importer.optimizeMeshPolygons || !importer.optimizeMeshVertices)
            {
                importer.optimizeMeshPolygons = true;
                importer.optimizeMeshVertices = true;
                changed = true;
            }

            if (!changed)
                continue;

            importer.SaveAndReimport();
            touched++;
        }

        return touched;
    }

    static int BuildMaterials()
    {
        Shader shader = Shader.Find("Standard");
        int made = 0;

        foreach (KeyValuePair<string, Color> entry in Palette)
        {
            string path = $"{Materials}/{entry.Key}.mat";

            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                continue;

            Material material = new Material(shader) { name = entry.Key, color = entry.Value };

            // Flat and matte. These are untextured blocks of colour and a specular highlight on
            // a tree trunk reads as wet plastic.
            material.SetFloat("_Glossiness", 0.05f);
            material.SetFloat("_Metallic", 0f);

            // The single most valuable setting on this whole asset: without it every one of a
            // hundred identical trees is its own draw call.
            material.enableInstancing = true;

            AssetDatabase.CreateAsset(material, path);
            made++;
        }

        return made;
    }

    /// Prints what was imported and what it costs, because "it should be fine" is not a number.
    static void Report()
    {
        int models = 0;
        long triangles = 0;
        int heaviest = 0;
        string worst = string.Empty;

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { Art }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                continue;

            models++;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;

                int count = filter.sharedMesh.triangles.Length / 3;
                triangles += count;

                if (count > heaviest)
                {
                    heaviest = count;
                    worst = prefab.name;
                }
            }
        }

        Debug.Log($"[jungle] {models} models, {triangles} triangles between them, "
                  + $"heaviest is {worst} at {heaviest}");
    }
}
