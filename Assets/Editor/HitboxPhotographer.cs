using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Builds a rig, builds hitboxes on it exactly the way a real spawn does, and renders both
/// together - the mesh and a translucent overlay matching each collider's real shape and size -
/// so alignment can be seen rather than assumed. roadmap.md flagged this as never having actually
/// been looked at next to the gorilla, despite the fitted-capsule rewrite; this is that look.
/// </summary>
public static class HitboxPhotographer
{
    public static string OutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "hitbox-check.png");

    static readonly Dictionary<string, Color> PartColour = new Dictionary<string, Color>
    {
        { "head",    new Color(1f, 0.15f, 0.15f, 0.55f) },
        { "neck",    new Color(1f, 0.5f, 0.1f, 0.5f) },
        { "chest",   new Color(1f, 0.9f, 0.1f, 0.45f) },
        { "stomach", new Color(0.6f, 1f, 0.1f, 0.45f) },
        { "hips",    new Color(0.1f, 1f, 0.4f, 0.45f) },
        { "leg",     new Color(0.1f, 0.6f, 1f, 0.45f) },
        { "arm",     new Color(0.7f, 0.2f, 1f, 0.45f) },
    };

    [MenuItem("Tools/Gorilla Warfare/Photograph the hitboxes")]
    public static void Run()
    {
        GameObject stand = new GameObject("~HitboxStand");
        MonkeyRig rig = stand.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Debug.LogError("[hitbox] MonkeyRig.Build refused");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        if (System.Environment.GetEnvironmentVariable("GW_HITBOX_DUMP_BONES") == "1")
        {
            foreach (Transform t in stand.GetComponentsInChildren<Transform>(true))
                Debug.Log($"[hitbox] bone: {t.name}");
        }

        int built = Hitbox.BuildFor(stand.transform, null);
        Debug.Log($"[hitbox] built {built} hitboxes");

        foreach (Hitbox box in stand.GetComponentsInChildren<Hitbox>(true))
        {
            if (box.TryGetComponent(out SphereCollider sc))
            {
                Debug.Log($"[hitbox] {box.partName,-8} sphere radius={sc.radius:F3} "
                          + $"lossyScale={box.transform.lossyScale}");
            }
            else if (box.TryGetComponent(out CapsuleCollider cc))
            {
                Debug.Log($"[hitbox] {box.partName,-8} capsule radius={cc.radius:F3} "
                          + $"height={cc.height:F3} center={cc.center} lossyScale={box.transform.lossyScale}");
            }
        }

        Material overlayMat = new Material(Shader.Find("Standard"))
        {
            name = "~overlay"
        };
        SetupTransparent(overlayMat);

        List<GameObject> overlays = new List<GameObject>();

        foreach (Hitbox box in stand.GetComponentsInChildren<Hitbox>(true))
        {
            GameObject overlay;

            if (box.TryGetComponent(out SphereCollider sphere))
            {
                overlay = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                overlay.transform.SetParent(box.transform, false);
                overlay.transform.localPosition = sphere.center;
                overlay.transform.localScale = Vector3.one * sphere.radius * 2f;
            }
            else if (box.TryGetComponent(out CapsuleCollider capsule))
            {
                overlay = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                overlay.transform.SetParent(box.transform, false);
                overlay.transform.localPosition = capsule.center;
                // Capsule direction 2 is local Z; the primitive's own capsule runs along Y, so
                // rotate it into that axis. Height on the primitive already includes the two
                // hemisphere caps, same convention Unity's CapsuleCollider uses.
                overlay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                overlay.transform.localScale = new Vector3(capsule.radius * 2f, capsule.height * 0.5f, capsule.radius * 2f);
            }
            else
            {
                continue;
            }

            Object.DestroyImmediate(overlay.GetComponent<Collider>());

            Color colour = PartColour.TryGetValue(box.partName, out Color c) ? c : new Color(1f, 1f, 1f, 0.4f);
            Material mine = new Material(overlayMat) { color = colour };
            overlay.GetComponent<Renderer>().material = mine;

            overlays.Add(overlay);
        }

        Bounds bounds = Bounds(stand);

        GameObject camHost = new GameObject("~HitboxCam");
        Camera cam = camHost.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.3f);
        float dist = radius * 2.2f;
        Vector3 eye = bounds.center + new Vector3(dist * 0.55f, dist * 0.35f, -dist * 0.75f);
        camHost.transform.position = eye;
        camHost.transform.LookAt(bounds.center + Vector3.up * radius * 0.15f, Vector3.up);

        GameObject lightHost = new GameObject("~HitboxLight");
        Light light = lightHost.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightHost.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

        GameObject fillHost = new GameObject("~HitboxFill");
        Light fill = fillHost.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.4f;
        fillHost.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

        int size = 800;
        RenderTexture rt = new RenderTexture(size, size, 24);
        RenderTexture prevActive = RenderTexture.active;

        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D shot = new Texture2D(size, size, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        shot.Apply();
        RenderTexture.active = prevActive;

        File.WriteAllBytes(OutputPath, shot.EncodeToPNG());
        Debug.Log($"[hitbox] -> {OutputPath}");

        Object.DestroyImmediate(stand);
        Object.DestroyImmediate(camHost);
        Object.DestroyImmediate(lightHost);
        Object.DestroyImmediate(fillHost);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(shot);

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void SetupTransparent(Material m)
    {
        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    static Bounds Bounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(root.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);

        return b;
    }
}
