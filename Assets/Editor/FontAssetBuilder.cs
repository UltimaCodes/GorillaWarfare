using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

// Turns the .ttf and .otf files in Assets/Fonts into TextMeshPro font assets.
//
// A raw font file is invisible to TMP. Dropping one into the project and expecting it in the
// font dropdown is the usual first surprise - TMP needs its own asset, built from the font,
// holding the atlas and the glyph table. This makes one per font next to the original.
//
// Dynamic atlases rather than pre-rendered ones: TMP adds glyphs to the texture as it meets
// them, so nothing has to be decided up front about which characters the menu will use, and a
// font used for three words doesn't carry an atlas built for the whole Latin set.
public static class FontAssetBuilder
{
    const string Folder = "Assets/Fonts";

    [MenuItem("Tools/Gorilla Warfare/Build font assets")]
    public static void Run()
    {
        List<string> made = new List<string>();
        List<string> failed = new List<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:Font", new[] { Folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Font font = AssetDatabase.LoadAssetAtPath<Font>(path);

            if (font == null)
                continue;

            string target = Path.Combine(Path.GetDirectoryName(path),
                                         Path.GetFileNameWithoutExtension(path) + " SDF.asset")
                                .Replace('\\', '/');

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(target) != null)
            {
                Debug.Log($"[font] {Path.GetFileName(target)} already exists, leaving it alone");
                continue;
            }

            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);

            if (asset == null)
            {
                failed.Add(Path.GetFileName(path));
                continue;
            }

            asset.name = Path.GetFileNameWithoutExtension(target);

            AssetDatabase.CreateAsset(asset, target);

            // The atlas and the material live inside the asset rather than beside it, so moving
            // or deleting the font asset takes them with it instead of leaving orphans.
            if (asset.atlasTextures != null && asset.atlasTextures.Length > 0)
            {
                asset.atlasTextures[0].name = asset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            }

            if (asset.material != null)
            {
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            EditorUtility.SetDirty(asset);
            made.Add(Path.GetFileName(target));
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        foreach (string name in made)
            Debug.Log($"[font] built {name}");

        foreach (string name in failed)
            Debug.LogError($"[font] could not build an asset from {name}");

        Debug.Log($"[font] {made.Count} built, {failed.Count} failed");

        if (Application.isBatchMode)
            EditorApplication.Exit(failed.Count == 0 ? 0 : 1);
    }
}
