using UnityEditor;
using UnityEngine;

// Batch-mode asset setup, so import settings are in version control as code rather than as
// something you have to remember to click.
// Run: Unity -batchmode -quit -executeMethod AssetFixups.All
public static class AssetFixups
{
    const string monkeyPath = "Assets/Resources/Models/Monkey/monkey.fbx";

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

        // bounds already have the current import scale baked in, so divide it back out to get
        // the true height. Without this, running twice measures the scaled mesh and resets the
        // factor to 1, which puts the model back to full size.
        float currentScale = Mathf.Approximately(importer.globalScale, 0f) ? 1f : importer.globalScale;
        float trueHeight = rawHeight / currentScale;

        // Aim for a hair under the 2 unit capsule so the feet aren't buried in the floor.
        const float targetHeight = 1.9f;
        float scale = trueHeight > 0.01f ? targetHeight / trueHeight : 0.4f;

        importer.globalScale = scale;

        // Rig None, not Humanoid. Humanoid solves the pose from muscle space every frame and
        // writing bone transforms directly is unsupported on it - the only way in is
        // SetBoneLocalRotation inside OnAnimatorIK. With an avatar and no controller you get the
        // avatar's default pose, which is a T-pose. That's exactly what was on screen.
        // We drive bones by hand, so we want nothing owning the skeleton.
        importer.animationType = ModelImporterAnimationType.None;
        importer.importAnimation = false;
        importer.SaveAndReimport();

        Debug.Log($"[fixups] monkey true height {trueHeight:F2} (measured {rawHeight:F2} at scale {currentScale:F4}) -> scale {scale:F4}");
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
