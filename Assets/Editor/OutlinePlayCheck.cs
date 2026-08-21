using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tests Custom/ScreenOutline the way it will actually run - inside a real Play Mode session,
/// through Unity's own per-frame render loop - rather than a one-shot editor Camera.Render()
/// call. OutlineCheck's manual renders showed no effect at all even with the depth+normals mode
/// forced on and a direct Graphics.Blit bypassing OnRenderImage entirely, which pointed at the
/// test method itself rather than the shader: DepthTextureMode's prepass and OnRenderImage's
/// callback are both scheduled as part of Unity's normal per-frame camera pipeline, and a manual,
/// single, editor-mode Render() call outside Play Mode is a different enough code path that it
/// may simply never trigger either one. This settles which one it actually was.
///
/// Same EnterPlaymode + RuntimeInitializeOnLoadMethod + SessionState pattern PlayModeProbe uses,
/// stripped down - no networking, no match, just a camera and a cube.
/// </summary>
public static class OutlinePlayCheck
{
    const string Flag = "GorillaWarfare.OutlinePlayCheck";

    [MenuItem("Tools/Gorilla Warfare/Photograph the screen outline (play mode)")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);
        new GameObject("~OutlinePlayCheck").AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.position = new Vector3(-1.2f, 0.5f, 3f);

            MonkeyRig rig = new GameObject("~Gorilla").AddComponent<MonkeyRig>();
            bool rigBuilt = rig.Build(false);
            rig.transform.position = new Vector3(1.2f, 0f, 3f);
            rig.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            Debug.Log($"[outline-play] rig built={rigBuilt}");

            GameObject camHost = new GameObject("~Cam");
            Camera cam = camHost.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.2f, -0.8f);
            cam.transform.LookAt(new Vector3(0f, 0.9f, 3f), Vector3.up);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.8f, 0.8f, 0.85f);

            GameObject lightHost = new GameObject("~Light");
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional;
            lightHost.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            // A few real frames before adding the outline, so there's a clean "before" to
            // compare the "after" against - same camera, same scene, only the component differs.
            // Real frames matter here: they're what run the camera through Unity's own per-frame
            // pipeline at least once before anything is captured from it.
            for (int i = 0; i < 3; i++) yield return null;
            Capture(cam, "before");

            camHost.AddComponent<ScreenOutline>();
            cam.depthTextureMode |= DepthTextureMode.DepthNormals;

            for (int i = 0; i < 5; i++) yield return null;
            Capture(cam, "after");

            Debug.Log("[outline-play] done");
            EditorApplication.Exit(0);
        }

        void Capture(Camera cam, string name)
        {
            // A manual Render() here, same technique OutlineCheck uses - but this camera has
            // already been through several real frames of Unity's own update loop first, which
            // OutlineCheck's version never was. If the depth+normals prepass and OnRenderImage
            // are scheduled off the normal per-frame pipeline rather than off Render() itself,
            // that prior real-frame warm-up is what a bare editor-mode Render() call was missing.
            int size = 640;
            RenderTexture rt = new RenderTexture(size, size, 24);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevTarget = cam.targetTexture;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D shot = new Texture2D(size, size, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            shot.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;

            string path = Path.Combine(Application.dataPath, "..", "Library", $"outline-play-{name}.png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Debug.Log($"[outline-play] {name} -> {path}");

            Object.Destroy(rt);
            Object.Destroy(shot);
        }
    }
}
