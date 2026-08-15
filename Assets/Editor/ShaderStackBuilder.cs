using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

// Points the game at the post-processing package's resources.
//
// PostProcessLayer refuses to do anything without a PostProcessResources asset, and the only
// copy of it lives inside the package. Nothing at runtime can load that by path: the package
// folder name carries a content hash that changes whenever the package is reinstalled, so
// today's "com.unity.postprocessing@94edadb6c5ab" is not tomorrow's.
//
// So this finds it once, here, where AssetDatabase can search by type, and writes a small
// holder into Resources with the reference in it. The reference survives because Unity stores
// it as a guid rather than a path.
public static class ShaderStackBuilder
{
    const string Folder = "Assets/Resources";
    const string Path = Folder + "/ShaderResources.asset";

    [MenuItem("Tools/Gorilla Warfare/Build the shader stack")]
    public static void Run()
    {
        string[] found = AssetDatabase.FindAssets("t:PostProcessResources");

        if (found.Length == 0)
        {
            Debug.LogError("[shaders] no PostProcessResources anywhere - is com.unity.postprocessing installed?");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        string resourcePath = AssetDatabase.GUIDToAssetPath(found[0]);
        PostProcessResources resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(resourcePath);

        if (!Directory.Exists(Folder))
            Directory.CreateDirectory(Folder);

        ShaderResources holder = AssetDatabase.LoadAssetAtPath<ShaderResources>(Path);
        bool making = holder == null;

        if (making)
            holder = ScriptableObject.CreateInstance<ShaderResources>();

        holder.resources = resources;

        if (making)
            AssetDatabase.CreateAsset(holder, Path);

        EditorUtility.SetDirty(holder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[shaders] {(making ? "created" : "updated")} {Path} pointing at {resourcePath}");

        // Prove every preset actually builds. Each one is a pile of Override calls against an
        // API that changes between package versions, and the failure mode is a preset that
        // silently produces an empty profile - which looks exactly like the effect being off.
        foreach (GameSettings.ShaderPreset preset in System.Enum.GetValues(typeof(GameSettings.ShaderPreset)))
        {
            int bare = Count(preset, false);
            int blurred = Count(preset, true);

            // Off means off. Anything else has to actually put effects in the profile, or the
            // preset is indistinguishable from Off and nobody would ever know.
            if (preset == GameSettings.ShaderPreset.Off)
            {
                if (bare != 0)
                    Debug.LogError($"[shaders] Off built {bare} effects - it is meant to build none");
            }
            else if (bare < 2)
            {
                Debug.LogError($"[shaders] preset {preset} built {bare} effects, expected at least 2");
            }

            // Motion blur is its own toggle and has to work regardless of the preset, including
            // on top of Off - that combination is the entire reason it isn't inside the presets.
            if (blurred != bare + 1)
                Debug.LogError($"[shaders] motion blur did not add exactly one effect to {preset} "
                               + $"({bare} without, {blurred} with)");

            Debug.Log($"[shaders] preset {preset}: {bare} effects, {blurred} with motion blur");
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static int Count(GameSettings.ShaderPreset preset, bool motionBlur)
    {
        PostProcessProfile profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        ShaderStack.BuildInto(profile, preset, motionBlur);

        int count = profile.settings.Count;
        Object.DestroyImmediate(profile);

        return count;
    }
}
