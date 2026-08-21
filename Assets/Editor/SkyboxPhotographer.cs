using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Creates Assets/Resources/Sky/JungleSky.mat if it's missing, assigns it as the scene's skybox,
/// and renders it from a couple of angles so the shader can actually be looked at - a shader
/// that fails to compile shows up as pink or as nothing at all, and reading the .shader source
/// doesn't catch that.
/// </summary>
public static class SkyboxPhotographer
{
    const string MaterialPath = "Assets/Resources/Sky/JungleSky.mat";

    public static string OutputPath(string suffix) =>
        Path.Combine(Application.dataPath, "..", "Library", $"skybox-check-{suffix}.png");

    [MenuItem("Tools/Gorilla Warfare/Photograph the skybox")]
    public static void Run()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (mat == null)
        {
            Shader shader = Shader.Find("Skybox/JungleSky");

            if (shader == null)
            {
                Debug.LogError("[sky] Skybox/JungleSky shader not found - check it compiled");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            mat = new Material(shader);

            if (!AssetDatabase.IsValidFolder("Assets/Resources/Sky"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                    AssetDatabase.CreateFolder("Assets", "Resources");
                AssetDatabase.CreateFolder("Assets/Resources", "Sky");
            }

            AssetDatabase.CreateAsset(mat, MaterialPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[sky] created {MaterialPath}");
        }

        RenderSettings.skybox = mat;

        GameObject camHost = new GameObject("~SkyCam");
        Camera cam = camHost.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.fieldOfView = 75f;
        cam.farClipPlane = 10f;

        // Unity pitches a camera DOWN for positive X - "look up" is negative X. Got this
        // backwards on the first pass here and mislabelled zenith/ground as a result; the
        // shader itself was never wrong, this test rig was pointing the wrong way.
        Shoot(cam, Quaternion.Euler(-5f, 0f, 0f), "horizon");
        Shoot(cam, Quaternion.Euler(-45f, 0f, 0f), "midsky");
        Shoot(cam, Quaternion.Euler(-85f, 0f, 0f), "zenith");
        Shoot(cam, Quaternion.Euler(20f, 0f, 0f), "ground");
        Shoot(cam, Quaternion.LookRotation(new Vector3(0.35f, 0.55f, -0.4f), Vector3.up), "sun");

        Object.DestroyImmediate(camHost);

        Debug.Log($"[sky] shader={mat.shader.name} isSupported={mat.shader.isSupported}");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Shoot(Camera cam, Quaternion rot, string name)
    {
        cam.transform.rotation = rot;

        int size = 480;
        RenderTexture rt = new RenderTexture(size, size, 24);
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D shot = new Texture2D(size, size, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        shot.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = null;

        File.WriteAllBytes(OutputPath(name), shot.EncodeToPNG());

        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(shot);
    }
}
