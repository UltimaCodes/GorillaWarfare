using UnityEditor;
using UnityEngine;

// Batch-mode asset setup, so import settings are in version control as code rather than as
// something you have to remember to click.
// Run: Unity -batchmode -quit -executeMethod AssetFixups.All
public static class AssetFixups
{
    const string monkeyPath = "Assets/Resources/Models/Gorilla/gorilla.fbx";

    public static void All()
    {
        ScaleMonkey();
        BuildBananaMaterial();
        FixAudioImports();
        AssetDatabase.SaveAssets();
    }

    // The mesh comes in about 5 units tall against a 2 unit CharacterController, so the monkey
    // was roughly two and a half times the size of his own collider.
    // Scale is baked into the fbx by the smd converter now, so this only sets the rig type and
    // builds the material. Applying an import scale on top is what left the model 100x oversized.
    public static void ScaleMonkey()
    {
        ModelImporter importer = AssetImporter.GetAtPath(monkeyPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[fixups] no importer at {monkeyPath}");
            return;
        }

        // useFileScale honours the fbx's own unit scale, which here is 0.01 - and the geometry
        // is already baked to metres by the converter, so it stacked and gave a 2cm gorilla.
        importer.useFileScale = false;

        // Measure and solve rather than hardcode. The exporter writes in fbx's centimetre
        // convention, so the geometry arrives 100x, but measuring means this stays correct if
        // the export ever changes. Height is on Y now that the axis conversion is baked in.
        GameObject probe = AssetDatabase.LoadAssetAtPath<GameObject>(monkeyPath);
        SkinnedMeshRenderer smr = probe != null ? probe.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        float measured = smr != null ? smr.sharedMesh.bounds.size.y : 0f;
        float current = Mathf.Approximately(importer.globalScale, 0f) ? 1f : importer.globalScale;
        float trueHeight = measured / current;

        if (trueHeight <= 0.001f)
        {
            Debug.LogError($"[fixups] cannot measure the mesh (height {trueHeight}). Not guessing.");
            return;
        }

        const float target = 1.9f;
        importer.globalScale = target / trueHeight;
        Debug.Log($"[fixups] height {trueHeight:F2} (measured {measured:F2} at {current:F4}) -> globalScale {importer.globalScale:F5}");

        // Generic. Not Humanoid, not None - both wrong here for different reasons:
        //   Humanoid solves the pose in muscle space and direct bone writes aren't supported,
        //   so with an avatar and no controller you get the avatar's default pose. A T-pose.
        //   None strips the rig entirely - verified, it imported SkinnedMeshRenderers: 0.
        // Generic keeps skin and bones and does no solving, so writing transforms just works.
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = false;
        importer.SaveAndReimport();

        BuildGorillaMaterial();
        Debug.Log("[fixups] gorilla: rig Generic, file scale, material built");
    }

    // The smd converter rebuilds geometry and weights from scratch and creates no materials, so
    // the model imported with Unity's Default-Material and no texture. Build one here and let
    // MonkeyRig assign it at runtime - more reliable than hoping fbx material remapping lines up.
    public static void BuildGorillaMaterial()
    {
        const string dir = "Assets/Resources/Models/Gorilla";
        const string matPath = dir + "/GorillaMat.mat";

        Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/TGorilla_Diffuse.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/TGorilla_Normal.png");

        // Unity has to be told a normal map is a normal map or it treats it as colour.
        TextureImporter ni = AssetImporter.GetAtPath(dir + "/TGorilla_Normal.png") as TextureImporter;
        if (ni != null && ni.textureType != TextureImporterType.NormalMap)
        {
            ni.textureType = TextureImporterType.NormalMap;
            ni.SaveAndReimport();
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/TGorilla_Normal.png");
        }

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, matPath);
        }

        if (diffuse != null) mat.SetTexture("_MainTex", diffuse);
        if (normal != null)
        {
            mat.SetTexture("_BumpMap", normal);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Glossiness", 0.1f);   // fur isn't shiny

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[fixups] material: diffuse={(diffuse != null)} normal={(normal != null)}");
    }

    // Bananas come out of a generator script, not a modelling package, so they arrive with no
    // usable material. One shared yellow for all of them.
    public static void BuildBananaMaterial()
    {
        const string path = "Assets/Resources/Models/Weapons/BananaMat.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }

        mat.color = new Color(0.94f, 0.79f, 0.16f);
        mat.SetFloat("_Glossiness", 0.35f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[fixups] banana material built");
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
