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
        rigGo.transform.localPosition = Vector3.zero;

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

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(diorama.transform, false);
        lightGo.transform.localPosition = new Vector3(1f, 2f, -1f);
        lightGo.transform.localRotation = Quaternion.Euler(35f, -35f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.None;

        GameObject camGo = new GameObject("Camera");
        camGo.transform.SetParent(diorama.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1f, -3.2f);
        camGo.transform.localRotation = Quaternion.identity;

        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 35f;
        cam.nearClipPlane = 0.1f;

        // Short on purpose - this camera only ever needs to see the one model 3.2m in front of
        // it, and a short far plane means it physically cannot render anything else even if a
        // future change put something else on whatever layer this ends up sharing.
        cam.farClipPlane = 10f;

        texture = new RenderTexture(textureSize, textureSize, 16) { name = "LoadingSpinnerRT" };
        cam.targetTexture = texture;

        GetComponent<RawImage>().texture = texture;
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
