using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

/// <summary>
/// The picture the game hands you after it has finished drawing the world.
///
/// The project has had the post-processing package installed, a volume sitting in the game
/// scene and a profile with bloom, occlusion and vignette in it since 2024 - and not one frame
/// of it has ever rendered. Post-processing needs a PostProcessLayer on the camera to do
/// anything at all, the camera is built at runtime by PlayerController, and nothing ever put
/// one there. So all of it has been decoration.
///
/// This builds the profile from the player's chosen preset instead of reading a fixed asset,
/// which is what makes the setting live: change the preset and the picture changes while you're
/// looking at it, rather than on the next launch.
///
/// Lives on the RoomManager object, which survives the trip between the menu and the game, and
/// re-attaches itself every time the camera is rebuilt - which is every respawn.
/// </summary>
public class ShaderStack : MonoBehaviour
{
    public static ShaderStack Instance { get; private set; }

    /// The layer the runtime volume lives on, and the only one the camera is told to look at.
    /// Already in the project's layer list from the original setup.
    const string VolumeLayerName = "PostProcessing";

    PostProcessVolume volume;
    PostProcessProfile profile;
    Camera attached;
    int volumeLayer;

    // ---- pulse ----
    //
    // The static preset picks a mood; this is what makes the picture itself react to a hit
    // instead of just the camera and the HUD. Added because everything that already flinches on
    // impact - Juice's shake, GameHud's own copy of it - moves things around but never touches
    // the actual image, which is exactly what a colour-grading/vignette/aberration stack is for.
    // Riding on Juice's own strength value rather than adding a second one, so a body shot and a
    // kill differ here by exactly the same ratio they already differ by everywhere else.
    Vignette vignetteRef;
    ChromaticAberration fringeRef;
    float baseVignette;
    float baseFringe;
    float pulseAmount;

    /// <param name="strength">0 to 1, same scale as Juice.Hit/Juice.Shake.</param>
    public static void Pulse(float strength)
    {
        if (Instance != null)
            Instance.pulseAmount = Mathf.Max(Instance.pulseAmount, Mathf.Clamp01(strength));
    }

    void Update()
    {
        if (pulseAmount <= 0.0001f)
            return;

        // Unscaled - a kill's own hitstop is usually dragging Time.timeScale down at the exact
        // moment this fires, and a pulse that only decays on scaled time would hang at full
        // strength for the whole freeze instead of actually reading as a pulse.
        pulseAmount = Mathf.MoveTowards(pulseAmount, 0f, Time.unscaledDeltaTime * 2.2f);

        if (vignetteRef != null)
            vignetteRef.intensity.value = baseVignette + pulseAmount * 0.3f;

        if (fringeRef != null)
            fringeRef.intensity.value = baseFringe + pulseAmount * 0.7f;
    }

    void Awake()
    {
        Instance = this;
        volumeLayer = LayerMask.NameToLayer(VolumeLayerName);

        if (volumeLayer < 0)
        {
            Debug.LogError($"[shaders] there is no '{VolumeLayerName}' layer - nothing will render");
            enabled = false;
            return;
        }

        GameSettings.Changed += Rebuild;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        SuppressAuthoredVolumes();
        Rebuild();
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                       UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        SuppressAuthoredVolumes();
    }

    /// <summary>
    /// Switches off any volume that came with a scene.
    ///
    /// The game scene has carried a global volume and a 2024 profile all along, and it has
    /// never rendered because nothing put a layer on the camera. Now that something does, it
    /// would quietly start applying a profile nobody has looked at in two years - and worse,
    /// it would keep applying it with the preset set to Off, so the one setting whose entire
    /// job is "no effects" would still have effects.
    ///
    /// Run on every scene load and on every camera attach rather than once, because this
    /// object is created in the menu and lives through the trip into the game: at the point
    /// Awake runs, the scene holding one volume has not loaded, and the player carrying the
    /// other has not spawned.
    ///
    /// Disabled rather than destroyed, so it's recoverable and so the scene file is untouched.
    /// </summary>
    void SuppressAuthoredVolumes()
    {
        foreach (PostProcessVolume other in FindObjectsByType<PostProcessVolume>(FindObjectsSortMode.None))
        {
            if (other == volume || !other.enabled)
                continue;

            Debug.Log($"[shaders] disabling the authored volume on {other.name} - "
                      + "the stack is built from your settings now");

            other.enabled = false;
        }
    }

    void OnDestroy()
    {
        GameSettings.Changed -= Rebuild;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
            Instance = null;

        // The profile is a ScriptableObject created in code, so nothing else will ever collect
        // it. Rebuilding on every settings change without this leaks one per change.
        if (profile != null)
            Destroy(profile);
    }

    // Late, so it runs after PlayerController has finished building this frame's camera.
    void LateUpdate()
    {
        Camera camera = PlayerController.LocalCamera;

        if (camera == null || camera == attached)
            return;

        attached = camera;
        Attach(camera);
    }

    void Attach(Camera camera)
    {
        PostProcessLayer layer = camera.GetComponent<PostProcessLayer>();

        if (layer == null)
            layer = camera.gameObject.AddComponent<PostProcessLayer>();

        PostProcessResources resources = ShaderResources.Load();

        if (resources == null)
        {
            layer.enabled = false;
            return;
        }

        layer.Init(resources);
        layer.volumeLayer = 1 << volumeLayer;
        layer.volumeTrigger = camera.transform;
        layer.enabled = true;

        // The player prefab's camera carries its own global volume, pointing at the same 2024
        // profile as the one in the scene. It has been dormant for the same reason everything
        // else was, and it arrives after the scene has finished loading - so the sweep on scene
        // load misses it entirely, and it comes back on every single respawn.
        SuppressAuthoredVolumes();

        ApplyAntialiasing(layer);
    }

    void ApplyAntialiasing(PostProcessLayer layer)
    {
        switch (GameSettings.Shaders)
        {
            case GameSettings.ShaderPreset.Off:
                layer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                break;

            case GameSettings.ShaderPreset.Clean:
                layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
                break;

            default:
                // Subpixel morphological, not temporal. TAA smears anything that moves, and in
                // a game where you strafe constantly and shoot at people who are also strafing,
                // a smeared enemy is a missed shot.
                layer.antialiasingMode = PostProcessLayer.Antialiasing.SubpixelMorphologicalAntialiasing;
                break;
        }
    }

    /// <summary>
    /// Throws the old profile away and builds a new one from the current preset.
    ///
    /// Rebuilt wholesale rather than having every effect permanently present and toggled,
    /// because an effect that's disabled still costs a little to evaluate and, more to the
    /// point, a profile with nine dormant settings in it is much harder to reason about than
    /// one that contains exactly what's switched on.
    /// </summary>
    public void Rebuild()
    {
        if (volume != null)
            Destroy(volume.gameObject);

        if (profile != null)
            Destroy(profile);

        if (attached != null)
        {
            PostProcessLayer layer = attached.GetComponent<PostProcessLayer>();
            if (layer != null)
                ApplyAntialiasing(layer);
        }

        vignetteRef = null;
        fringeRef = null;
        pulseAmount = 0f;

        if (GameSettings.Shaders == GameSettings.ShaderPreset.Off && !GameSettings.MotionBlur)
            return;

        profile = ScriptableObject.CreateInstance<PostProcessProfile>();

        BuildInto(profile, GameSettings.Shaders, GameSettings.MotionBlur);

        GameObject host = new GameObject("~ShaderVolume") { layer = volumeLayer };
        host.transform.SetParent(transform, false);

        volume = host.AddComponent<PostProcessVolume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.profile = profile;

        // Only wired up if the current preset already put these in the profile - a pulse has
        // nothing to push on top of if Clean's own vignette was never added, and Off already
        // returned above. Nobody who picked a lighter preset for their eyes or their framerate
        // should get the heavier one's effects smuggled in through a hit reaction.
        if (profile.TryGetSettings(out Vignette vignette))
        {
            vignetteRef = vignette;
            baseVignette = vignette.intensity.value;
        }

        if (profile.TryGetSettings(out ChromaticAberration fringe))
        {
            fringeRef = fringe;
            baseFringe = fringe.intensity.value;
        }
    }

    /// <summary>
    /// The presets themselves. Public and static so a check can build one without a camera,
    /// a scene or a game running.
    /// </summary>
    public static void BuildInto(PostProcessProfile profile, GameSettings.ShaderPreset preset,
                                 bool motionBlur)
    {
        if (preset != GameSettings.ShaderPreset.Off)
        {
            // Occlusion first, and in every preset that isn't Off, because it's the one effect
            // that adds information rather than mood - it's what makes a corner read as a
            // corner and a dark gorilla read as separate from the wall behind it.
            AmbientOcclusion ao = profile.AddSettings<AmbientOcclusion>();
            ao.enabled.Override(true);

            // Multi-scale is better and needs compute shaders. Anyone whose machine can't do
            // that gets the scalable one rather than nothing.
            ao.mode.Override(SystemInfo.supportsComputeShaders
                ? AmbientOcclusionMode.MultiScaleVolumetricObscurance
                : AmbientOcclusionMode.ScalableAmbientObscurance);

            ao.intensity.Override(preset == GameSettings.ShaderPreset.Overripe ? 1.6f : 0.9f);
            ao.radius.Override(preset == GameSettings.ShaderPreset.Overripe ? 0.4f : 0.25f);

            Bloom bloom = profile.AddSettings<Bloom>();
            bloom.enabled.Override(true);

            switch (preset)
            {
                case GameSettings.ShaderPreset.Clean:
                    bloom.intensity.Override(1.2f);
                    bloom.threshold.Override(1.15f);
                    break;

                case GameSettings.ShaderPreset.Full:
                    bloom.intensity.Override(3f);
                    bloom.threshold.Override(0.95f);
                    bloom.softKnee.Override(0.6f);
                    break;

                default:
                    // Deliberately past the point of good taste. Muzzle flashes smear, the
                    // yellow of a banana blows out, and the whole thing glows like it's wet.
                    bloom.intensity.Override(7f);
                    bloom.threshold.Override(0.7f);
                    bloom.softKnee.Override(0.85f);
                    bloom.diffusion.Override(8f);
                    break;
            }
        }

        if (preset == GameSettings.ShaderPreset.Full || preset == GameSettings.ShaderPreset.Overripe)
        {
            ColorGrading grading = profile.AddSettings<ColorGrading>();
            grading.enabled.Override(true);
            grading.gradingMode.Override(GradingMode.LowDefinitionRange);

            if (preset == GameSettings.ShaderPreset.Full)
            {
                grading.contrast.Override(12f);
                grading.saturation.Override(10f);
                grading.postExposure.Override(0.2f);
            }
            else
            {
                // The Cruelty Squad end of the dial: colours that clash on purpose, pushed
                // warm, contrast hard enough to lose detail in the shadows.
                grading.contrast.Override(40f);
                grading.saturation.Override(55f);
                grading.temperature.Override(18f);
                grading.tint.Override(-12f);
                grading.postExposure.Override(0.35f);
            }

            Vignette vignette = profile.AddSettings<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(preset == GameSettings.ShaderPreset.Overripe ? 0.55f : 0.3f);
            vignette.smoothness.Override(0.45f);
        }

        if (preset == GameSettings.ShaderPreset.Overripe)
        {
            ChromaticAberration fringe = profile.AddSettings<ChromaticAberration>();
            fringe.enabled.Override(true);
            fringe.intensity.Override(0.45f);

            Grain grain = profile.AddSettings<Grain>();
            grain.enabled.Override(true);
            grain.intensity.Override(0.35f);
            grain.size.Override(1.6f);
            grain.colored.Override(true);
        }

        // Its own toggle, outside the presets. Motion blur is the one effect people either want
        // or actively cannot stand, and which of those you are has nothing to do with how
        // powerful your machine is.
        if (motionBlur)
        {
            MotionBlur blur = profile.AddSettings<MotionBlur>();
            blur.enabled.Override(true);
            blur.shutterAngle.Override(180f);
            blur.sampleCount.Override(8);
        }
    }
}
