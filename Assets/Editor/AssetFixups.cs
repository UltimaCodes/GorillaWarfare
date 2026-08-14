using UnityEditor;
using UnityEngine;

// Batch-mode asset setup, so import settings are in version control as code rather than as
// something you have to remember to click.
// Run: Unity -batchmode -quit -executeMethod AssetFixups.All
public static class AssetFixups
{
    const string monkeyPath = "Assets/3DModels/Monkey/monkey.fbx";

    public static void All()
    {
        ScaleMonkey();
        FixAudioImports();
        AssetDatabase.SaveAssets();
    }

    // The mesh comes in about 5 units tall against a 2 unit CharacterController, so the monkey
    // was roughly two and a half times the size of his own collider.
    public static void ScaleMonkey()
    {
        ModelImporter importer = AssetImporter.GetAtPath(monkeyPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[fixups] no importer at {monkeyPath}");
            return;
        }

        GameObject before = AssetDatabase.LoadAssetAtPath<GameObject>(monkeyPath);
        SkinnedMeshRenderer skin = before != null ? before.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        float rawHeight = skin != null ? skin.sharedMesh.bounds.size.y : 0f;

        // Aim for a hair under the 2 unit capsule so the feet aren't buried in the floor.
        const float targetHeight = 1.9f;
        float scale = rawHeight > 0.01f ? targetHeight / rawHeight : 0.4f;

        importer.globalScale = scale;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        Debug.Log($"[fixups] monkey raw height {rawHeight:F2} -> scale {scale:F4} (target {targetHeight})");
    }

    // The gunshots are 96kHz stereo, which is studio-master quality for a sound that plays for
    // a fifth of a second in a game. Mono and compressed is a fraction of the size and nobody
    // can tell - and mono matters because positional audio needs it to pan properly.
    public static void FixAudioImports()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Resources/Audio" });
        int changed = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null)
                continue;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 44100;

            // preloadAudioData moved onto the per-platform sample settings in newer Unity.
            settings.preloadAudioData = true;

            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.loadInBackground = false;

            importer.SaveAndReimport();
            changed++;
        }

        Debug.Log($"[fixups] audio clips retuned: {changed}");
    }
}
