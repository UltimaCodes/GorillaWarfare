using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for what the skins and crates are made of, so a reimport can't quietly undo
/// them: the pattern pack's tiles (Assets/Art/Patterns) are repeating masks, not colour; the
/// particle pack's sprites (Resources/Particles/Finish) are small, see-through and clamped; the
/// crate chests (Assets/Art/Crates) keep their animations, as legacy clips the crate screen plays.
/// All three are Kenney's, CC0 - see CREDITS.md.
/// </summary>
public class SkinAssetSetup : AssetPostprocessor
{
    const string Patterns = "Assets/Art/Patterns/";
    const string Sprites = "Assets/Resources/Particles/Finish/";
    const string Crates = "Assets/Art/Crates/";

    void OnPreprocessTexture()
    {
        TextureImporter importer = (TextureImporter)assetImporter;

        if (assetPath.StartsWith(Patterns))
        {
            // Read as a mask - white is where the finish's second colour goes - so linear, and it
            // tiles across the weapon.
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 256;
            importer.alphaSource = TextureImporterAlphaSource.None;
        }
        else if (assetPath.StartsWith(Sprites))
        {
            // Sprites, so the crate screen's UI can use them as well as the particles.
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 256;
        }
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Crates))
            return;

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Legacy;
        importer.importAnimation = true;
    }

    /// Reimports all three folders under the settings above, and adds the preview layer.
    [MenuItem("Tools/Gorilla Warfare/Set up the skin and crate assets")]
    public static void Run()
    {
        ProjectLayers.EnsureLayers();

        foreach (string folder in new[] { Patterns, Sprites, Crates })
        {
            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[skins] pattern, sprite and chest imports set; preview layer "
                  + (LayerMask.NameToLayer(PreviewStage.LayerName) >= 0 ? "present" : "MISSING"));

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
