using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A little studio somewhere nobody can see, with its own camera and lights, drawing what's on it
/// into a texture the menus show - a weapon in a finish for the inventory and the crate reveal, the
/// crates' chests, and the finish cards' pictures.
///
/// Everything on it is on the Preview layer: its camera sees only that, and nothing else in any
/// scene lights it or sees it (every other light has the layer taken out of its mask as scenes
/// load), so a preview looks the same in the menu as it does mid-match. Each screen makes its own
/// stage (Create), far apart, so they never share a frame.
/// </summary>
public class PreviewStage : MonoBehaviour
{
    public const string LayerName = "Preview";

    public static int Layer => LayerMask.NameToLayer(LayerName);

    static readonly List<PreviewStage> stages = new List<PreviewStage>();
    static int made;

    public Camera Camera { get; private set; }
    public Transform Turntable { get; private set; }
    public RenderTexture Target { get; private set; }

    Light key;
    Light fill;
    Light back;

    /// Degrees a second the turntable turns on its own. The inventory lets you drag it too.
    public float spin = 18f;

    SingleShotGun gun;
    string gunWeapon;

    /// <summary>
    /// A new stage, drawn into a texture this size. `background` is what's behind whatever is on
    /// it - the screens pass their own panel colour, so the picture sits in the page.
    /// </summary>
    public static PreviewStage Create(string name, int width, int height, Color background)
    {
        GameObject root = new GameObject($"~PreviewStage {name}");
        DontDestroyOnLoad(root);

        // A kilometre below and apart from each other - under every map, and never in one
        // another's frame.
        root.transform.position = new Vector3(made * 60f, -1000f, 0f);
        made++;

        PreviewStage stage = root.AddComponent<PreviewStage>();
        stage.Build(width, height, background);
        return stage;
    }

    void Build(int width, int height, Color background)
    {
        int layer = Layer;
        if (layer < 0)
        {
            Debug.LogWarning("[skins] no Preview layer - run Tools/Gorilla Warfare/Set up the skin and crate assets");
            layer = 0;
        }

        gameObject.layer = layer;

        Turntable = new GameObject("Turntable").transform;
        Turntable.SetParent(transform, false);
        Turntable.gameObject.layer = layer;

        Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = $"{name} target", antiAliasing = 4 };
        Target.Create();

        GameObject cameraHost = new GameObject("Camera");
        cameraHost.transform.SetParent(transform, false);
        cameraHost.layer = layer;
        Camera = cameraHost.AddComponent<Camera>();
        Camera.clearFlags = CameraClearFlags.SolidColor;
        Camera.backgroundColor = background;
        Camera.cullingMask = 1 << layer;
        Camera.fieldOfView = 30f;
        Camera.nearClipPlane = 0.05f;
        Camera.farClipPlane = 40f;
        Camera.targetTexture = Target;
        Camera.transform.localPosition = new Vector3(0f, 0.15f, -2.2f);
        Camera.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);

        // The same toon outline the game draws, so a preview looks like the game.
        cameraHost.AddComponent<ScreenOutline>();

        key = MakeLight("Key", new Vector3(35f, -35f, 0f), new Color(1f, 0.96f, 0.88f), 1.15f, layer);
        fill = MakeLight("Fill", new Vector3(10f, 140f, 0f), new Color(0.7f, 0.8f, 1f), 0.45f, layer);
        back = MakeLight("Back", new Vector3(-20f, 175f, 0f), new Color(1f, 1f, 1f), 0.8f, layer);
        key.shadows = LightShadows.Soft;

        stages.Add(this);
        KeepOtherLightsOff();
    }

    Light MakeLight(string lightName, Vector3 euler, Color colour, float intensity, int layer)
    {
        GameObject host = new GameObject(lightName);
        host.transform.SetParent(transform, false);
        host.transform.localRotation = Quaternion.Euler(euler);
        host.layer = layer;

        Light light = host.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = colour;
        light.intensity = intensity;
        light.cullingMask = 1 << layer;
        return light;
    }

    void OnDestroy()
    {
        stages.Remove(this);
        if (Target != null)
            Target.Release();
    }

    void LateUpdate()
    {
        if (spin != 0f && Turntable != null)
            Turntable.Rotate(0f, spin * Time.unscaledDeltaTime, 0f, Space.World);
    }

    /// Turn it by hand - the inventory's drag.
    public void TurnBy(float degrees) => Turntable.Rotate(0f, degrees, 0f, Space.World);

    /// Only draws while something's showing it - a stage costs a camera's worth of rendering.
    public bool Drawing
    {
        get => Camera != null && Camera.enabled;
        set { if (Camera != null) Camera.enabled = value; }
    }

    // ---------------------------------------------------------------- weapons

    /// <summary>
    /// A weapon on the turntable, in a finish (null is stock) - the real model, built the way the
    /// game builds it, framed to fill the picture side on.
    /// </summary>
    public SingleShotGun ShowWeapon(string weapon, WeaponFinish finish, float across = 1.25f)
    {
        if (gun == null || gunWeapon != weapon)
        {
            ClearTurntable();

            GunInfo info = Resources.Load<GunInfo>(WeaponLoadout.GunResourcePath + weapon);
            if (info == null)
                return null;

            GameObject held = new GameObject(weapon);
            held.transform.SetParent(Turntable, false);
            gun = held.AddComponent<SingleShotGun>();
            gun.Configure(info, null, false);
            gun.enabled = false;
            Hitbox.Neutralise(held.transform);
            gunWeapon = weapon;

            // Side on, pointing right, a little tipped towards the camera.
            held.transform.localRotation = Quaternion.Euler(0f, 90f, -8f);
            Frame(held.transform, across);
        }

        WeaponSkins.Apply(gun, finish);
        SetLayer(gun.transform, Layer);
        return gun;
    }

    /// Anything else on the turntable - a chest - framed the same way, `across` metres at its widest.
    public GameObject Show(GameObject prefab, float yaw = 0f, float across = 0.85f)
    {
        ClearTurntable();
        if (prefab == null)
            return null;

        GameObject shown = Instantiate(prefab, Turntable);
        shown.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        SetLayer(shown.transform, Layer);
        Frame(shown.transform, across);
        return shown;
    }

    /// <summary>
    /// Something placed on the stage itself rather than the turntable - where the caller wants
    /// it, `across` metres at its widest, standing on `localPosition`. The crate screen lays its
    /// three chests out this way and moves them itself.
    /// </summary>
    public GameObject Place(GameObject prefab, Vector3 localPosition, float yaw, float across)
    {
        if (prefab == null)
            return null;

        GameObject placed = Instantiate(prefab, transform);
        placed.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        SetLayer(placed.transform, Layer);

        Bounds bounds = BoundsOf(placed.transform);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        placed.transform.localScale *= across / Mathf.Max(largest, 0.001f);

        bounds = BoundsOf(placed.transform);
        Vector3 standOn = transform.TransformPoint(localPosition);
        placed.transform.position += new Vector3(standOn.x - bounds.center.x, standOn.y - bounds.min.y, standOn.z - bounds.center.z);
        return placed;
    }

    public void ClearTurntable()
    {
        for (int i = Turntable.childCount - 1; i >= 0; i--)
            Destroy(Turntable.GetChild(i).gameObject);

        gun = null;
        gunWeapon = null;
        Turntable.localRotation = Quaternion.identity;
    }

    /// Centres it on the turntable and scales it to `across` metres at its widest - 1.25 at the
    /// camera's distance is about two thirds of the picture.
    void Frame(Transform shown, float across = 1.25f)
    {
        Bounds bounds = BoundsOf(shown);
        if (bounds.size == Vector3.zero)
            return;

        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float scale = across / Mathf.Max(largest, 0.001f);
        shown.localScale *= scale;

        bounds = BoundsOf(shown);
        shown.position += Turntable.position - bounds.center;
    }

    public static Bounds BoundsOf(Transform root)
    {
        Bounds bounds = new Bounds(root.position, Vector3.zero);
        bool started = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled)
                continue;
            if (!started) { bounds = r.bounds; started = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }

    public static void SetLayer(Transform root, int layer)
    {
        if (layer < 0)
            return;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ---------------------------------------------------------------- stills

    static readonly Dictionary<(string, string), Texture2D> icons = new Dictionary<(string, string), Texture2D>();
    static PreviewStage iconStage;

    /// <summary>
    /// A still of a weapon in a finish, made once and kept - the finish cards' pictures. Taken
    /// on a stage of its own at the card's size.
    /// </summary>
    public static Texture2D Icon(string weapon, WeaponFinish finish, Color background)
    {
        string finishKey = finish != null ? finish.key : "";
        if (icons.TryGetValue((weapon, finishKey), out Texture2D cached) && cached != null)
            return cached;

        if (iconStage == null)
        {
            iconStage = Create("icons", 256, 192, background);
            iconStage.spin = 0f;
            iconStage.Drawing = false;
        }

        iconStage.Camera.backgroundColor = background;
        iconStage.ShowWeapon(weapon, finish);
        iconStage.Turntable.localRotation = Quaternion.Euler(0f, -20f, 0f);
        iconStage.Camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = iconStage.Target;
        Texture2D still = new Texture2D(iconStage.Target.width, iconStage.Target.height, TextureFormat.RGB24, false)
        {
            name = $"~icon {weapon} {finishKey}",
        };
        still.ReadPixels(new Rect(0, 0, still.width, still.height), 0, 0);
        still.Apply();
        RenderTexture.active = previous;

        icons[(weapon, finishKey)] = still;
        return still;
    }

    // ---------------------------------------------------------------- the rest of the world

    /// The Preview layer out of every light that isn't a stage's, so a scene's own sun never
    /// lights a preview - they'd look different in every map.
    static void KeepOtherLightsOff()
    {
        int layer = Layer;
        if (layer < 0)
            return;

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.gameObject.layer != layer)
                light.cullingMask &= ~(1 << layer);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void WatchScenes() => SceneManager.sceneLoaded += (scene, mode) => KeepOtherLightsOff();
}
