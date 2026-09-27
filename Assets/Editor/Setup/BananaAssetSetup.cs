using UnityEditor;
using UnityEngine;

/// <summary>
/// Configures the sourced banana sprite's import settings - Unity gives any new PNG a default
/// texture import (compressed, bilinear filtered, not marked as a Sprite), which is exactly
/// wrong for a small hand-pixelled sprite meant to stay crisp at HUD scale. Idempotent, like
/// this project's other one-shot maintenance tools - safe to rerun if the settings are ever
/// reset by a reimport.
/// </summary>
public static class BananaAssetSetup
{
    const string Path = "Assets/Textures/UI/BananaHealth.png";

    [MenuItem("Tools/Gorilla Warfare/Configure banana sprite import")]
    public static void Run()
    {
        TextureImporter importer = AssetImporter.GetAtPath(Path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError($"[banana] no texture at {Path}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = 19f;   // native width, so a 1x1 unit Image is native size

        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

        Debug.Log("[banana] import settings applied");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
