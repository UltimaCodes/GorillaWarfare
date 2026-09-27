using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A player model spinning in the middle of the loading screen - "I want you to improve it by
/// adding a player model in the middle that just spins around like the gorilla spinning meme."
///
/// Builds its own self-contained diorama (model, camera, light) rendered to a RenderTexture
/// rather than depending on whatever 3D scene happens to be behind this UI - the loading screen
/// shows up exactly when a scene transition is in progress, which is the one moment there's no
/// guarantee a normal game camera exists at all, let alone one pointed somewhere useful. Placed
/// far from true world origin so it can't be seen by (or accidentally lit by/collide with)
/// anything real, rather than needing a dedicated culling-mask layer for one spinning prop.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class LoadingScreenSpinner : MonoBehaviour
{
    [SerializeField] float spinDegreesPerSecond = 140f;
    [SerializeField] int textureSize = 512;

    // Far enough that no real camera's far clip plane or culling reaches it, close enough that
    // float precision on positions/physics still behaves normally.
    static readonly Vector3 DioramaOrigin = new Vector3(0f, 20000f, 0f);

    Transform model;
    RenderTexture texture;
    GameObject diorama;

    void Awake()
    {
        diorama = new GameObject("~LoadingSpinnerDiorama");
        diorama.transform.position = DioramaOrigin;

        GameObject rigGo = new GameObject("Model");
        rigGo.transform.SetParent(diorama.transform, false);

        // A metre up: MonkeyRig puts the model a metre below its root (a player capsule's centre),
        // so at zero the gorilla sat below the camera's line and was cut off at the waist.
        rigGo.transform.localPosition = Vector3.up;

        MonkeyRig rig = rigGo.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Debug.LogError("[loading] could not build a spinner model");
            Destroy(diorama);
            return;
        }

        // No gait animation needed for a model that never walks - Update would otherwise keep
        // fighting the idle pose every frame for nothing.
        rig.enabled = false;

        // A colour, not the default white - a plain white-lit model against a plain background
        // reads as a placeholder capsule, not a character. Reuses PlayerColours rather than
        // inventing a one-off tint, so this at least looks like a real player's own gorilla.
        rig.Tint(PlayerColours.Palette[0]);

        model = rigGo.transform;

        // A point light with a short range, not a directional one. A directional light reaches the
        // whole scene however far away it's parked - this one was lighting the entire main menu
        // backdrop, washing out every colour behind the menu, for as long as the menu was open.
        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(diorama.transform, false);
        lightGo.transform.localPosition = new Vector3(1.6f, 2.6f, -2.4f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 8f;
        light.intensity = 2.2f;
        light.shadows = LightShadows.None;

        // The texture takes the shape of the image it's drawn into, so resizing the Spinner object
        // by hand widens the view rather than stretching the gorilla.
        Rect rect = ((RectTransform)transform).rect;
        float aspect = rect.width > 1f && rect.height > 1f ? rect.width / rect.height : 1f;

        int width = aspect >= 1f ? textureSize : Mathf.RoundToInt(textureSize * aspect);
        int height = aspect >= 1f ? Mathf.RoundToInt(textureSize / aspect) : textureSize;

        GameObject camGo = new GameObject("Camera");
        camGo.transform.SetParent(diorama.transform, false);
        camGo.transform.localRotation = Quaternion.identity;

        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.1f;
        cam.aspect = aspect;

        // Framed from the model itself rather than a fixed distance. A fixed 3.2m cut the arm
        // off at the sides - it swings furthest from the middle as the model turns, which a
        // front-on framing never sees. Reported by the person who asked for the spinner.
        Frame(cam, rigGo.transform, aspect);

        texture = new RenderTexture(width, height, 16) { name = "LoadingSpinnerRT" };
        cam.targetTexture = texture;

        GetComponent<RawImage>().texture = texture;

        // Only while this screen is actually showing - see OnEnable/OnDisable.
        diorama.SetActive(isActiveAndEnabled);
    }

    /// <summary>
    /// Backs the camera off until the whole spin fits.
    ///
    /// Measured from the posed mesh's actual vertices: the furthest any of them sits from the
    /// spin axis is the radius of the cylinder the model sweeps as it turns, and the camera has
    /// to fit that cylinder's nearest face - which looms larger than the axis - inside both the
    /// vertical and the horizontal field of view. A little margin so nothing touches the edge.
    /// </summary>
    static void Frame(Camera cam, Transform pivot, float aspect)
    {
        const float Margin = 1.1f;

        float radius = 0f;
        float bottom = float.MaxValue;
        float top = float.MinValue;
        Mesh baked = new Mesh();

        foreach (SkinnedMeshRenderer skin in pivot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            skin.BakeMesh(baked, true);
            Matrix4x4 toWorld = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);

            foreach (Vector3 vertex in baked.vertices)
            {
                Vector3 world = toWorld.MultiplyPoint3x4(vertex);
                Vector3 fromAxis = world - pivot.position;

                radius = Mathf.Max(radius, new Vector2(fromAxis.x, fromAxis.z).magnitude);
                bottom = Mathf.Min(bottom, world.y);
                top = Mathf.Max(top, world.y);
            }
        }

        Object.Destroy(baked);

        // Nothing to measure - keep the old fixed framing rather than a camera inside the model.
        if (radius <= 0f || top <= bottom)
        {
            cam.transform.position = pivot.position + new Vector3(0f, 0f, -3.2f);
            cam.farClipPlane = 10f;
            return;
        }

        float halfHeight = (top - bottom) * 0.5f * Margin;
        float tanVertical = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHorizontal = tanVertical * aspect;

        float distance = Mathf.Max(radius * Margin / tanHorizontal, halfHeight / tanVertical) + radius;

        cam.transform.position = new Vector3(pivot.position.x, (top + bottom) * 0.5f, pivot.position.z - distance);

        // Short on purpose - this camera only ever needs to see the one model, and a short far
        // plane means it physically cannot render anything else even if a future change put
        // something else on whatever layer this ends up sharing.
        cam.farClipPlane = distance + radius + 1f;
    }

    // The diorama is its own root, so it doesn't switch off with the loading screen by itself -
    // it went on rendering its camera (and lighting the scene) behind every other menu.
    void OnEnable()
    {
        if (diorama != null)
            diorama.SetActive(true);
    }

    void OnDisable()
    {
        if (diorama != null)
            diorama.SetActive(false);
    }

    void Update()
    {
        if (model != null)
            model.Rotate(Vector3.up, spinDegreesPerSecond * Time.unscaledDeltaTime, Space.World);
    }

    void OnDestroy()
    {
        if (diorama != null)
            Destroy(diorama);

        if (texture != null)
            Destroy(texture);
    }
}
