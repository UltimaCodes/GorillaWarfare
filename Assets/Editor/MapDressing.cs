using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Replaces the map's placeholder textures with something you can actually fight in.
//
// The map was surfaced with screenshots and memes - a giant eyeball on one wall, embers on
// another. Funny, and genuinely unplayable: the gorilla is dark brown, and against a busy dark
// texture there is no silhouette to pick out. This isn't the M7 art pass, it's the minimum
// needed to see an enemy.
//
// Three surfaces, three jobs:
//   floor        mid tone with a grid, so you can judge distance and your own speed
//   outer walls  near black, so the edge of the map reads as a boundary rather than a room
//   inner walls  bright and warm, so a dark player crossing one is unmissable
//
// The grid is generated rather than sourced, which keeps it CC0 by construction and means the
// line weight can be tuned by editing a number instead of hunting for another texture.
public static class MapDressing
{
    const string Folder = "Assets/Materials/Generated";
    const string ScenePath = "Assets/Scenes/Game.unity";

    // Unity's built in plane is 10 units across at scale 1.
    const float PlaneUnits = 10f;

    // One grid square per metre.
    const float MetresPerTile = 1f;

    [MenuItem("Tools/Gorilla Warfare/Redress the map")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);

        Texture2D grid = SaveTexture(BuildGrid(), "GridTile");
        Texture2D panel = SaveTexture(BuildPanel(), "PanelTile");

        Material floor = BuildMaterial("MapFloor", new Color(0.46f, 0.47f, 0.50f), grid, 0.15f);
        Material outer = BuildMaterial("MapWallOuter", new Color(0.09f, 0.09f, 0.12f), panel, 0.05f);
        Material inner = BuildMaterial("MapWallInner", new Color(0.80f, 0.74f, 0.62f), panel, 0.08f);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int painted = 0;
        painted += Paint(scene, "Floor", floor, grid);
        painted += Paint(scene, "OuterWalls", outer, panel);
        painted += Paint(scene, "InnerWalls", inner, panel);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log($"[map] repainted {painted} surfaces");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    // Every renderer under a group whose name starts with the given prefix. The map is built as
    // Floor / OuterWalls / InnerWalls / InnerWalls (1), so a prefix match catches the duplicate.
    static int Paint(Scene scene, string groupPrefix, Material material, Texture2D texture)
    {
        List<Renderer> found = new List<Renderer>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(groupPrefix))
                    continue;

                found.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
        }

        // Tiling goes on the material, not a property block. A block override of _MainTex_ST
        // is silently ignored by the Standard shader in the built in pipeline, which is why the
        // first attempt at this produced a flat grey floor with no grid on it at all.
        //
        // That means one tiling for the whole group, so it's taken from the largest surface in
        // it - the big pieces are the ones you spend time looking at, and a smaller piece
        // showing slightly larger squares is not something anyone will notice mid fight.
        float biggest = 0f;
        Vector3 reference = Vector3.one;

        foreach (Renderer renderer in found)
        {
            renderer.sharedMaterial = material;

            // Clear any block a previous run left behind.
            renderer.SetPropertyBlock(null);

            Vector3 size = renderer.bounds.size;
            float area = size.x * size.y + size.y * size.z + size.z * size.x;

            if (area > biggest)
            {
                biggest = area;
                reference = size;
            }
        }

        // The face you look at is the two largest dimensions - the third is the thickness.
        // Sorting rather than picking, because a floor plane has a near zero Y and a wall has a
        // near zero X or Z, and any hand rolled comparison gets one of those wrong.
        float[] dimensions = { reference.x, reference.y, reference.z };
        System.Array.Sort(dimensions);

        material.mainTextureScale = new Vector2(
            Mathf.Max(1f, Mathf.Round(dimensions[2] / MetresPerTile)),
            Mathf.Max(1f, Mathf.Round(dimensions[1] / MetresPerTile)));

        EditorUtility.SetDirty(material);

        Debug.Log($"[map] {groupPrefix}: {found.Count} surfaces, tiling {material.mainTextureScale}");

        return found.Count;
    }

    static Material BuildMaterial(string name, Color colour, Texture2D texture, float gloss)
    {
        string path = $"{Folder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = Shader.Find("Standard");
        material.color = colour;
        material.mainTexture = texture;
        material.SetFloat("_Glossiness", gloss);
        material.SetFloat("_Metallic", 0f);

        EditorUtility.SetDirty(material);
        return material;
    }

    static Texture2D SaveTexture(Texture2D generated, string name)
    {
        string path = $"{Folder}/{name}.png";
        File.WriteAllBytes(path, generated.EncodeToPNG());
        Object.DestroyImmediate(generated);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 8;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // A square with a darker border. Tiled, that's a grid.
    static Texture2D BuildGrid()
    {
        const int size = 256;
        const int line = 6;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onLine = x < line || y < line || x >= size - line || y >= size - line;

                // A little variation inside the square so a big floor isn't a flat colour.
                float grain = 0.94f + Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.12f;
                float value = onLine ? 0.62f : grain;

                texture.SetPixel(x, y, new Color(value, value, value));
            }
        }

        texture.Apply();
        return texture;
    }

    // Flat with a faint horizontal banding, so walls have a sense of scale without patterning
    // strongly enough to hide anyone standing in front of them.
    static Texture2D BuildPanel()
    {
        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGB24, true);

        for (int y = 0; y < size; y++)
        {
            float band = y % 64 < 3 ? 0.88f : 1f;

            for (int x = 0; x < size; x++)
            {
                float grain = 0.96f + Mathf.PerlinNoise(x * 0.03f, y * 0.03f) * 0.08f;
                float value = band * grain;
                texture.SetPixel(x, y, new Color(value, value, value));
            }
        }

        texture.Apply();
        return texture;
    }
}
