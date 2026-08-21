using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Renders a gorilla and a pineapple with their outlines applied exactly the way MonkeyRig.Build
/// and Projectile.BuildVisual actually apply them, rather than trusting the shader compiles and
/// assuming it looks right - the skybox shader looked broken in a screenshot when it wasn't, and
/// the inverse failure mode (looks fine on paper, wrong on screen) is just as real for an outline
/// pass that gets its winding order or its pass order wrong.
/// </summary>
public static class OutlineCheck
{
    public static string OutputPath(string name) =>
        Path.Combine(Application.dataPath, "..", "Library", $"outline-check-{name}.png");

    [MenuItem("Tools/Gorilla Warfare/Photograph the outlines")]
    public static void Run()
    {
        GameObject camHost = new GameObject("~OutlineCam");
        Camera cam = camHost.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.85f, 0.85f, 0.88f);
        cam.nearClipPlane = 0.01f;

        if (System.Environment.GetEnvironmentVariable("GW_OUTLINE_SCREEN") == "1")
        {
            camHost.AddComponent<ScreenOutline>();
            // Awake() timing for a component added via AddComponent outside Play Mode isn't
            // guaranteed the way it is during real gameplay - set this directly rather than
            // trust it fired, so a missing Awake() call isn't mistaken for the shader itself
            // being broken.
            cam.depthTextureMode |= DepthTextureMode.DepthNormals;
            Debug.Log($"[outline] depthTextureMode={cam.depthTextureMode}");
        }

        GameObject lightHost = new GameObject("~OutlineLight");
        Light light = lightHost.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightHost.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        // --- gorilla, via the real MonkeyRig.Build path ---
        GameObject stand = new GameObject("~OutlineGorilla");
        MonkeyRig rig = stand.AddComponent<MonkeyRig>();
        bool built = rig.Build(false);

        if (built)
        {
            if (System.Environment.GetEnvironmentVariable("GW_OUTLINE_DUMP") == "1")
                DumpMeshStats(stand);

            Bounds b = RendererBounds(stand);
            Frame(cam, b, 2.2f);
            Snap(cam, "gorilla");

            // Isolates the outline pass - strips every material slot down to just the outline
            // one, so the expanded shell itself can be seen without the real mesh drawn on top
            // of (and mostly hiding) it. Answers "is the shell itself gappy" directly instead of
            // inferring it from how it looks combined with the main render.
            if (System.Environment.GetEnvironmentVariable("GW_OUTLINE_ISOLATE") == "1")
            {
                foreach (Renderer r in stand.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    if (mats.Length > 0)
                        r.sharedMaterials = new[] { mats[0] };
                }
                Snap(cam, "gorilla-outline-only");
            }
        }
        else
        {
            Debug.LogError("[outline] MonkeyRig.Build refused");
        }

        Object.DestroyImmediate(stand);

        // --- pineapple, via the real Projectile.BuildVisual path (reflection - private method) ---
        GunInfo pineappleInfo = Resources.Load<GunInfo>("Guns/Pineapple");
        GameObject prefab = Resources.Load<GameObject>("Models/Weapons/Pineapple")
                            ?? Resources.Load<GameObject>("Models/Weapons/BananaPineapple");

        if (pineappleInfo != null && prefab != null)
        {
            GameObject projHost = new GameObject("~OutlinePineapple");
            Projectile proj = projHost.AddComponent<Projectile>();

            var buildVisual = typeof(Projectile).GetMethod("BuildVisual",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var infoField = typeof(Projectile).GetField("info",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (infoField != null) infoField.SetValue(proj, pineappleInfo);
            if (buildVisual != null) buildVisual.Invoke(proj, null);

            Bounds b = RendererBounds(projHost);
            Frame(cam, b, 2.6f);
            Snap(cam, "pineapple");

            Object.DestroyImmediate(projHost);
        }
        else
        {
            Debug.LogWarning("[outline] pineapple GunInfo or model not found, skipped");
        }

        Object.DestroyImmediate(camHost);
        Object.DestroyImmediate(lightHost);

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Frame(Camera cam, Bounds b, float distMul)
    {
        float radius = Mathf.Max(b.extents.magnitude, 0.05f);
        float dist = radius * distMul;
        cam.transform.position = b.center + new Vector3(dist * 0.6f, dist * 0.35f, -dist * 0.75f);
        cam.transform.LookAt(b.center, Vector3.up);
    }

    static void Snap(Camera cam, string name)
    {
        int size = 640;
        RenderTexture rt = new RenderTexture(size, size, 24);
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        // Isolates the shader from OnRenderImage's own reliability in a manual, editor-mode
        // Camera.Render() call - which is a real, separate variable from whether the shader math
        // is correct. If GW_OUTLINE_BLIT is set, re-runs the outline pass with an explicit
        // Graphics.Blit directly on the rendered colour, using whatever depth+normals texture
        // the camera just populated, rather than relying on Unity to call OnRenderImage itself.
        if (System.Environment.GetEnvironmentVariable("GW_OUTLINE_BLIT") == "1")
        {
            Shader shader = Shader.Find("Custom/ScreenOutline");
            if (shader != null)
            {
                Material mat = new Material(shader);
                RenderTexture rt2 = new RenderTexture(size, size, 0);
                Graphics.Blit(rt, rt2, mat);

                RenderTexture.active = rt2;
                Texture2D blitShot = new Texture2D(size, size, TextureFormat.RGB24, false);
                blitShot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                blitShot.Apply();
                File.WriteAllBytes(OutputPath(name + "-blit"), blitShot.EncodeToPNG());
                Debug.Log($"[outline] {name}-blit -> {OutputPath(name + "-blit")}");

                Object.DestroyImmediate(rt2);
                Object.DestroyImmediate(blitShot);
                Object.DestroyImmediate(mat);
            }
        }

        RenderTexture.active = rt;
        Texture2D shot = new Texture2D(size, size, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        shot.Apply();
        RenderTexture.active = prevActive;
        cam.targetTexture = null;

        File.WriteAllBytes(OutputPath(name), shot.EncodeToPNG());
        Debug.Log($"[outline] {name} -> {OutputPath(name)}");

        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(shot);
    }

    static void DumpMeshStats(GameObject root)
    {
        SkinnedMeshRenderer skin = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || skin.sharedMesh == null)
        {
            Debug.Log("[outline-dump] no SkinnedMeshRenderer found");
            return;
        }

        Mesh baked = new Mesh();
        skin.BakeMesh(baked, true);

        Vector3[] verts = baked.vertices;
        Vector3[] normals = baked.normals;

        Debug.Log($"[outline-dump] verts={verts.Length} normals={normals.Length} "
                  + $"lossyScale={skin.transform.lossyScale} rootScale={root.transform.lossyScale}");

        Bounds vb = new Bounds(verts.Length > 0 ? verts[0] : Vector3.zero, Vector3.zero);
        foreach (Vector3 v in verts) vb.Encapsulate(v);
        Debug.Log($"[outline-dump] local vertex bounds size={vb.size} centre={vb.center}");

        float minLen = float.MaxValue, maxLen = 0f, sumLen = 0f;
        int zeroCount = 0, nanCount = 0;
        foreach (Vector3 n in normals)
        {
            float len = n.magnitude;
            if (float.IsNaN(len) || float.IsInfinity(len)) { nanCount++; continue; }
            if (len < 0.01f) { zeroCount++; continue; }
            minLen = Mathf.Min(minLen, len);
            maxLen = Mathf.Max(maxLen, len);
            sumLen += len;
        }
        Debug.Log($"[outline-dump] normal length min={minLen:F4} max={maxLen:F4} "
                  + $"avg={sumLen / Mathf.Max(1, normals.Length):F4} zero={zeroCount} nan={nanCount}");

        // Sample a handful of raw vertex/normal pairs so an actual value can be read, not just
        // aggregate stats that could hide a localised problem.
        for (int i = 0; i < verts.Length; i += Mathf.Max(1, verts.Length / 12))
        {
            Debug.Log($"[outline-dump] v[{i}] pos={verts[i]} normal={normals[i]} "
                      + $"len={normals[i].magnitude:F4}");
        }

        Object.DestroyImmediate(baked);
    }

    static Bounds RendererBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one * 0.1f);

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);

        return b;
    }
}
