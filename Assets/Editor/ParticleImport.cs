using UnityEditor;
using UnityEngine;

// Imports the particle PNGs as sprites rather than as textures.
//
// Unity's default for a PNG is a plain texture, and Resources.LoadAll<Sprite> on a folder full
// of those returns nothing at all - no error, no warning, just an empty array and a muzzle flash
// that silently never appears. Worth a script precisely because the failure is invisible.
public static class ParticleImport
{
    static readonly string[] Folders =
    {
        "Assets/Art/Particles",
        "Assets/Resources/Particles",
    };

    [MenuItem("Tools/Gorilla Warfare/Import the particle sprites")]
    public static void Run()
    {
        int touched = 0;

        foreach (string folder in Folders)
        {
            if (!AssetDatabase.IsValidFolder(folder))
                continue;

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

                if (importer == null)
                    continue;

                bool changed = false;

                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }

                // These are 256px flashes drawn additively over a moving camera for a twentieth
                // of a second. Nobody has ever seen the detail in one, and a quarter of the
                // memory is a quarter of the memory.
                if (importer.maxTextureSize > 128)
                {
                    importer.maxTextureSize = 128;
                    changed = true;
                }

                if (!importer.mipmapEnabled)
                {
                    // On, because these are drawn in the world at wildly varying distances and
                    // an unmipped sprite at range crawls with aliasing.
                    importer.mipmapEnabled = true;
                    changed = true;
                }

                if (importer.alphaIsTransparency != true)
                {
                    importer.alphaIsTransparency = true;
                    changed = true;
                }

                if (!changed)
                    continue;

                importer.SaveAndReimport();
                touched++;
            }
        }

        AssetDatabase.Refresh();

        int muzzle = Resources.LoadAll<Sprite>("Particles/Muzzle").Length;
        int boom = Resources.LoadAll<Sprite>("Particles/Boom").Length;

        Debug.Log($"[particles] {touched} imported as sprites - "
                  + $"{muzzle} muzzle shapes, {boom} explosion shapes reachable from Resources");

        if (muzzle == 0 || boom == 0)
            Debug.LogError("[particles] a folder came back empty - the effects will not appear");

        if (Application.isBatchMode)
            EditorApplication.Exit(muzzle > 0 && boom > 0 ? 0 : 1);
    }
}
