using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Any map from above and from eye height - to look at a layout rather than trust the numbers.
/// Edit mode, so no grass (it grows at runtime) and none of the post stack. Output:
/// Logs/map-shots/&lt;scene&gt;-*.png.
///
/// GW_MAP_SCENE is the scene's name (Zoo, Game, Glacier). GW_MAP_VIEWS lists the shots, each
/// "name:x,y,z,yaw[,pitch[,fov]]" separated by ';' (y as "~1.8" is 1.8m above the ground there);
/// without it there's just the one from above.
/// Menu backdrop spots are chosen the same way - 55 degrees, pitch -2, the menu camera's own.
/// </summary>
public static class MapPhotographer
{
    [MenuItem("Tools/Gorilla Warfare/Photograph a map")]
    public static void Run()
    {
        string sceneName = System.Environment.GetEnvironmentVariable("GW_MAP_SCENE");
        if (string.IsNullOrEmpty(sceneName))
            sceneName = "Zoo";

        string path = $"Assets/Scenes/{sceneName}.unity";
        if (!File.Exists(path))
        {
            Debug.LogError($"[shots] no {path} to photograph");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        Physics.SyncTransforms();

        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "map-shots");
        Directory.CreateDirectory(folder);

        // How far the map reaches, for the shot from above.
        Bounds extent = new Bounds(Vector3.zero, Vector3.one * 20f);
        foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            Vector3 size = terrain.terrainData.size;
            extent.Encapsulate(new Bounds(terrain.transform.position + size * 0.5f, size));
        }

        GameObject host = new GameObject("~MapCamera");
        Camera camera = host.AddComponent<Camera>();
        camera.farClipPlane = 600f;
        camera.nearClipPlane = 0.05f;

        void Shot(string name, Vector3 at, Quaternion look, bool fromAbove, float fov)
        {
            camera.orthographic = fromAbove;
            camera.orthographicSize = Mathf.Max(extent.extents.x, extent.extents.z) + 4f;
            camera.clearFlags = fromAbove ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = fov;
            host.transform.SetPositionAndRotation(at, look);

            int width = fromAbove ? 1400 : 1920, height = fromAbove ? 1400 : 1080;
            RenderTexture target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            camera.Render();

            RenderTexture.active = target;
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = null;

            File.WriteAllBytes(Path.Combine(folder, $"{sceneName}-{name}.png"), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            target.Release();
        }

        Shot("top", new Vector3(extent.center.x, extent.max.y + 60f, extent.center.z),
             Quaternion.LookRotation(Vector3.down, Vector3.forward), true, 60f);

        int taken = 1;
        string views = System.Environment.GetEnvironmentVariable("GW_MAP_VIEWS");
        if (!string.IsNullOrEmpty(views))
        {
            foreach (string spot in views.Split(';'))
            {
                string[] named = spot.Split(':');
                if (named.Length != 2)
                    continue;

                string[] v = named[1].Split(',');
                if (v.Length < 4)
                    continue;

                float Parse(int i, float fallback) => i < v.Length ? float.Parse(v[i].TrimStart('~'), CultureInfo.InvariantCulture) : fallback;
                Vector3 at = new Vector3(Parse(0, 0f), Parse(1, 0f), Parse(2, 0f));

                // "~1.8" for the height is that far above the ground there - the terrain, not a tree
                // top - the way the menu camera is placed on the floor.
                Terrain under = Terrain.activeTerrain;
                if (v[1].StartsWith("~") && under != null)
                    at.y += under.SampleHeight(at) + under.transform.position.y;
                // Pitch as the camera's own x angle - positive looks down.
                Shot(named[0].Trim(), at, Quaternion.Euler(Parse(4, -2f), Parse(3, 0f), 0f), false, Parse(5, 70f));
                taken++;
            }
        }

        Object.DestroyImmediate(host);
        Debug.Log($"[shots] {taken} shot(s) of {sceneName} in {folder}");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
