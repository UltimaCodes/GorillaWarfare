using UnityEditor;
using UnityEngine;
using System.IO;

/// <summary>
/// Renders the peel exactly as SingleShotGun poses it and saves a PNG, so the melee hold can be
/// checked by looking at it rather than guessed a third time - it's been wrong twice already,
/// both times from reasoning about the rotation rather than seeing it.
/// </summary>
public static class PeelPhotographer
{
    public static string OutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "peel-hold-check.png");

    /// Read from the GW_PEEL_EULER env var when set ("x,y,z"), so a rotation can be tried
    /// without writing it to the asset first - only once a value is confirmed by eye does it
    /// belong on GunInfo. Falls back to whatever's actually on the asset.
    static Vector3 HoldOverride(Vector3 fallback)
    {
        string raw = System.Environment.GetEnvironmentVariable("GW_PEEL_EULER");
        if (string.IsNullOrEmpty(raw))
            return fallback;

        string[] parts = raw.Split(',');
        if (parts.Length != 3) return fallback;

        return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
    }

    [MenuItem("Tools/Gorilla Warfare/Photograph the peel")]
    public static void Run()
    {
        // GW_PEEL_MODEL lets this double as a calibration shot - a normal gun is identity, no
        // meleeHold at all, so rendering one through the exact same rig shows what "correctly
        // forward, as this camera and these axes actually read it" looks like, rather than
        // trusting an assumption about which way +Z projects on screen.
        string weaponName = System.Environment.GetEnvironmentVariable("GW_PEEL_MODEL");
        if (string.IsNullOrEmpty(weaponName)) weaponName = "Peel";

        GunInfo info = Resources.Load<GunInfo>($"Guns/{weaponName}");
        GameObject prefab = Resources.Load<GameObject>($"Models/Weapons/Banana{weaponName}")
                            ?? Resources.Load<GameObject>($"Models/Weapons/{weaponName}");

        if (info == null || prefab == null)
        {
            Debug.LogError($"[peel] missing GunInfo or model for {weaponName}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        GameObject rig = new GameObject("~PeelPhotoRig");

        GameObject model = Object.Instantiate(prefab, rig.transform);
        model.transform.localPosition = Vector3.zero;

        Vector3 hold = HoldOverride(info.meleeHold);

        // Exactly SingleShotGun's own pose maths: identity, then Info.meleeHold. Not the whole
        // Awake path (AnchorGrip only repositions, it doesn't rotate), so this is the true
        // orientation without needing the rest of the weapon-building machinery running.
        Quaternion heldRot = Quaternion.Euler(hold);

        // GW_PEEL_SWING=1 previews the stab, exactly as StabSwing computes it: the swing is a
        // further local rotation on top of held, not an independent Euler angle, so it has to
        // be composed as a quaternion product to match what the game actually does.
        bool swing = System.Environment.GetEnvironmentVariable("GW_PEEL_SWING") == "1";
        model.transform.localRotation = swing
            ? heldRot * Quaternion.Euler(-info.meleeSwing, 0f, 0f)
            : heldRot;

        Material mat = Resources.Load<Material>($"Models/Weapons/{weaponName}Mat")
                       ?? Resources.Load<Material>($"Models/Weapons/Banana{weaponName}Mat");
        if (mat != null)
        {
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }

        Bounds bounds = Bounds(model);

        GameObject camHost = new GameObject("~PeelCam");
        Camera cam = camHost.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
        cam.fieldOfView = 40f;
        cam.nearClipPlane = 0.01f;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);

        // A ground plane so "up" is unambiguous in the render - a banana floating with nothing
        // to sit on is genuinely hard to judge as upside down or not from a single still image.
        // Scaled and coloured off the model's own size rather than a fixed number, so it reads
        // as a floor under the object instead of the object vanishing into an oversized white
        // sheet - which is what a stock 10x10 Plane does next to a 0.2m banana.
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(bounds.center.x, bounds.min.y - radius * 0.15f, bounds.center.z);
        ground.transform.localScale = Vector3.one * (radius * 0.5f);
        ground.GetComponent<Renderer>().material.color = new Color(0.2f, 0.35f, 0.15f);

        float dist = radius * 3.2f;

        // GW_PEEL_VIEW=front puts the camera where the player's eye would be, looking straight
        // down +Z - the one angle that unambiguously answers "does this point at me" rather than
        // needing to be read off an oblique three-quarter shot and a set of axis markers.
        bool front = System.Environment.GetEnvironmentVariable("GW_PEEL_VIEW") == "front";

        Vector3 eye = front
            ? bounds.center + new Vector3(0f, radius * 0.15f, -dist)
            : bounds.center + new Vector3(dist * 0.7f, dist * 0.5f, -dist * 0.7f);

        camHost.transform.position = eye;
        camHost.transform.LookAt(bounds.center, Vector3.up);

        GameObject lightHost = new GameObject("~PeelLight");
        Light light = lightHost.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.9f;
        light.shadows = LightShadows.Soft;
        lightHost.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

        // A second, dimmer light from the opposite side so the far face of the banana isn't
        // pure black - a single directional light on a convex shape makes half of it silhouette,
        // which is exactly the wrong failure mode when the question is "which way is it curving".
        GameObject fillHost = new GameObject("~PeelFill");
        Light fill = fillHost.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.35f;
        fillHost.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

        // Axis markers at the model's own origin (where the hand grip sits), so the render can
        // be read in absolute terms - which way is forward (blue, +Z, the direction the item
        // holder faces when aiming) and which way is up (green, +Y) - rather than only judging
        // the banana's shape against itself.
        GameObject axes = new GameObject("~PeelAxes");
        axes.transform.position = model.transform.position;
        Axis(axes.transform, Vector3.forward * radius, new Color(0.2f, 0.4f, 1f));   // Z, forward
        Axis(axes.transform, Vector3.up * radius, new Color(0.2f, 1f, 0.3f));        // Y, up
        Axis(axes.transform, Vector3.right * radius, new Color(1f, 0.25f, 0.25f));   // X, right

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

        string path = OutputPath;
        File.WriteAllBytes(path, shot.EncodeToPNG());

        Debug.Log($"[peel] rendered hold={hold} (asset meleeHold={info.meleeHold}) -> {path}");

        Object.DestroyImmediate(rig);
        Object.DestroyImmediate(camHost);
        Object.DestroyImmediate(lightHost);
        Object.DestroyImmediate(fillHost);
        Object.DestroyImmediate(ground);
        Object.DestroyImmediate(axes);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(shot);

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Axis(Transform parent, Vector3 tip, Color colour)
    {
        GameObject rod = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(rod.GetComponent<Collider>());
        rod.transform.SetParent(parent, false);

        float len = tip.magnitude;
        rod.transform.localPosition = tip * 0.5f;
        rod.transform.up = tip.normalized;
        rod.transform.localScale = new Vector3(len * 0.06f, len * 0.5f, len * 0.06f);
        rod.GetComponent<Renderer>().material.color = colour;
    }

    static Bounds Bounds(GameObject root)
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
