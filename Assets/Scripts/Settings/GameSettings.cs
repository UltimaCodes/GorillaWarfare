using System;
using UnityEngine;

/// <summary>
/// Everything the player can change about the game, saved to PlayerPrefs.
///
/// Static, so anything can read it without holding a reference, and loaded before the first
/// scene so the values are right from frame one rather than being applied a moment later - a
/// game that starts at the wrong sensitivity and corrects itself is worse than one that just
/// starts wrong, because you've already moved the mouse.
///
/// Every setter writes the preference immediately and raises <see cref="Changed"/>. Nothing
/// here waits for an apply button: the crosshair redraws while you drag the slider, and the
/// shader stack rebuilds while you cycle the preset. You can see what a setting does, which is
/// the only way anyone actually picks a value rather than leaving the default.
///
/// The one thing deliberately not saved is which weapon you were holding. Settings are about
/// the client; anything about the match lives in Photon's room properties.
/// </summary>
public static class GameSettings
{
    /// Raised after any setting changes. Live listeners rather than polling, because most of
    /// these are read once at spawn and would otherwise not take effect until you died.
    public static event Action Changed;

    // ---------------------------------------------------------------- aim

    public const float MinSensitivity = 0.5f;
    public const float MaxSensitivity = 15f;

    public static float Sensitivity { get; private set; } = 3f;
    public static bool InvertY { get; private set; }

    /// <summary>
    /// How much slower the mouse is while scoped, as a fraction.
    ///
    /// Separate from plain sensitivity because aiming narrows the field of view, and a narrower
    /// view magnifies every movement - the same wrist flick that crosses half the screen hip
    /// fired crosses all of it scoped. The weapon already scales sensitivity by its own zoom
    /// ratio; this is the personal multiplier on top, for people who want to be slower still.
    /// </summary>
    public static float AdsSensitivity { get; private set; } = 1f;

    /// Press-to-latch instead of hold-to-aim. A motor-accessibility option (holding a button for
    /// the length of every engagement isn't free for everyone) as much as a preference some
    /// players just have.
    public static bool AimToggle { get; private set; }

    // ---------------------------------------------------------------- audio

    public static float MasterVolume { get; private set; } = 0.8f;
    public static float SfxVolume { get; private set; } = 1f;
    public static float MusicVolume { get; private set; } = 0.6f;

    // ---------------------------------------------------------------- screen

    public const float MinFov = 60f;
    public const float MaxFov = 120f;

    public static float Fov { get; private set; } = 90f;
    public static bool Fullscreen { get; private set; } = true;
    public static int ScreenWidth { get; private set; }
    public static int ScreenHeight { get; private set; }

    /// Index into Unity's own quality levels, which drive shadows, texture resolution and
    /// anisotropic filtering. Separate from the shader stack below - one is how much detail is
    /// in the world, the other is what happens to the picture afterwards.
    public static int QualityLevel { get; private set; } = -1;

    // ---------------------------------------------------------------- shaders

    public enum ShaderPreset
    {
        /// Nothing at all. The fallback for anyone whose machine hates this.
        Off,

        /// Ambient occlusion and a little bloom. Makes corners read as corners without
        /// announcing itself.
        Clean,

        /// Bloom, occlusion, grading, vignette. What the game is meant to look like.
        Full,

        /// Everything, pushed past taste - crushed colour, heavy bloom, chromatic aberration
        /// and grain. The Cruelty Squad end of the dial.
        Overripe,
    }

    public static ShaderPreset Shaders { get; private set; } = ShaderPreset.Full;

    /// Motion blur is its own toggle rather than part of a preset, because it's the one effect
    /// people either want or actively cannot stand, and that has nothing to do with how
    /// powerful their machine is.
    public static bool MotionBlur { get; private set; }

    /// Same reasoning as motion blur: a vibe, not a quality tier, so it sits outside the preset
    /// ladder rather than as a fifth entry in it. Colour-depth-and-dither only - see
    /// PsxFilter.shader for why vertex snapping and affine texture warping aren't part of this.
    /// On by default - "REALLY like the psx filter," reported directly, once it was actually
    /// strong enough to see. Everyone still gets the toggle to turn it back off.
    public static bool PsxFilter { get; private set; } = true;

    // ---------------------------------------------------------------- joke settings
    //
    // "Add some joke settings too." Real PlayerPrefs-backed entries, same as everything else on
    // this screen, not fake controls wired to nothing.

    /// Deliberately inert - "a setting that's exactly as real as the rest of the menu right up
    /// until you ask what it actually changes." Briefly wired to a goofy cryptid-sighting effect
    /// (BigfootSighting.cs) and then pulled back out at direct request - "remove for now" - so
    /// this is back to being the joke it started as rather than a half-finished feature.
    public static bool BelieveInBigfoot { get; private set; }

    /// The other deliberately inert joke setting - a slider with nothing behind it.
    public static float MonkeyBusinessLevel { get; private set; } = 50f;

    // ---------------------------------------------------------------- crosshair

    public static float CrosshairSize { get; private set; } = 14f;
    public static float CrosshairThickness { get; private set; } = 3f;
    public static float CrosshairGap { get; private set; } = 14f;
    public static bool CrosshairDot { get; private set; }
    public static bool CrosshairOutline { get; private set; } = true;
    public static Color CrosshairColour { get; private set; } = Color.white;

    /// <summary>
    /// Use one crosshair for everything, rather than each weapon's own.
    ///
    /// Off by default, because a shotgun and a sniper wanting different reticles is a feature.
    /// On for anyone who has a crosshair they like and does not want to retune it five times -
    /// which was the complaint, and it is a fair one.
    /// </summary>
    public static bool CrosshairOverride { get; private set; }

    /// Whether the crosshair opens with the weapon's spread. On by default because it tells you
    /// something true, off for anyone who wants a fixed reticle to aim with.
    public static bool CrosshairDynamic { get; private set; } = true;

    // ---------------------------------------------------------------- storage

    const string DefaultPrefix = "gw_";

    // Not const: PlayModeProbe points it somewhere else for the length of a run. Batch mode
    // shares the Editor's own PlayerPrefs, so writing through the real prefix meant every probe
    // run wiped the settings used in normal Play Mode.
    static string Prefix = DefaultPrefix;

    /// <summary>
    /// Reads and writes every setting under a different PlayerPrefs prefix, then reloads from
    /// it. For batch-mode tests only. Null or empty goes back to the real prefix.
    /// </summary>
    public static void UsePrefsNamespace(string prefix)
    {
        Prefix = string.IsNullOrEmpty(prefix) ? DefaultPrefix : prefix;
        Load();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Load()
    {
        Sensitivity = Get(nameof(Sensitivity), 3f);
        AdsSensitivity = Get(nameof(AdsSensitivity), 1f);
        InvertY = Get(nameof(InvertY), false);
        AimToggle = Get(nameof(AimToggle), false);

        MasterVolume = Get(nameof(MasterVolume), 0.8f);
        SfxVolume = Get(nameof(SfxVolume), 1f);
        MusicVolume = Get(nameof(MusicVolume), 0.6f);

        Fov = Get(nameof(Fov), 90f);
        Fullscreen = Get(nameof(Fullscreen), true);
        ScreenWidth = Get(nameof(ScreenWidth), Screen.currentResolution.width);
        ScreenHeight = Get(nameof(ScreenHeight), Screen.currentResolution.height);
        QualityLevel = Get(nameof(QualityLevel), QualitySettings.GetQualityLevel());

        Shaders = (ShaderPreset)Get(nameof(Shaders), (int)ShaderPreset.Full);
        MotionBlur = Get(nameof(MotionBlur), false);
        PsxFilter = Get(nameof(PsxFilter), true);

        BelieveInBigfoot = Get(nameof(BelieveInBigfoot), false);
        MonkeyBusinessLevel = Get(nameof(MonkeyBusinessLevel), 50f);

        CrosshairSize = Get(nameof(CrosshairSize), 14f);
        CrosshairThickness = Get(nameof(CrosshairThickness), 3f);
        CrosshairGap = Get(nameof(CrosshairGap), 14f);
        CrosshairDot = Get(nameof(CrosshairDot), false);
        CrosshairOutline = Get(nameof(CrosshairOutline), true);
        CrosshairDynamic = Get(nameof(CrosshairDynamic), true);
        CrosshairOverride = Get(nameof(CrosshairOverride), false);
        CrosshairColour = new Color(Get("CrosshairR", 1f), Get("CrosshairG", 1f), Get("CrosshairB", 1f));

        ApplyAudio();
        ApplyScreen();
        ApplyQuality();

        // No Changed here. Nothing has subscribed yet - this runs before the first scene loads -
        // and everything that reads settings does so on its own Awake anyway.
    }

    static float Get(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
    static int Get(string key, int fallback) => PlayerPrefs.GetInt(Prefix + key, fallback);
    static bool Get(string key, bool fallback) => PlayerPrefs.GetInt(Prefix + key, fallback ? 1 : 0) == 1;

    static void Put(string key, float value) => PlayerPrefs.SetFloat(Prefix + key, value);
    static void Put(string key, int value) => PlayerPrefs.SetInt(Prefix + key, value);
    static void Put(string key, bool value) => PlayerPrefs.SetInt(Prefix + key, value ? 1 : 0);

    static void Announce()
    {
        // PlayerPrefs only reaches disk on Save, and a crash between changing a setting and
        // quitting cleanly would otherwise lose it. It's a handful of floats; the cost of
        // writing them every time somebody moves a slider is not worth measuring.
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- setters

    public static void SetSensitivity(float value)
    {
        Sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        Put(nameof(Sensitivity), Sensitivity);
        Announce();
    }

    public static void SetAdsSensitivity(float value)
    {
        AdsSensitivity = Mathf.Clamp(value, 0.1f, 2f);
        Put(nameof(AdsSensitivity), AdsSensitivity);
        Announce();
    }

    public static void SetInvertY(bool value)
    {
        InvertY = value;
        Put(nameof(InvertY), value);
        Announce();
    }

    public static void SetAimToggle(bool value)
    {
        AimToggle = value;
        Put(nameof(AimToggle), value);
        Announce();
    }

    public static void SetMasterVolume(float value)
    {
        MasterVolume = Mathf.Clamp01(value);
        Put(nameof(MasterVolume), MasterVolume);
        ApplyAudio();
        Announce();
    }

    public static void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        Put(nameof(SfxVolume), SfxVolume);
        Announce();
    }

    public static void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        Put(nameof(MusicVolume), MusicVolume);
        Announce();
    }

    public static void SetFov(float value)
    {
        Fov = Mathf.Clamp(value, MinFov, MaxFov);
        Put(nameof(Fov), Fov);
        ApplyFov();
        Announce();
    }

    public static void SetFullscreen(bool value)
    {
        Fullscreen = value;
        Put(nameof(Fullscreen), value);
        ApplyScreen();
        Announce();
    }

    public static void SetResolution(int width, int height)
    {
        ScreenWidth = Mathf.Max(640, width);
        ScreenHeight = Mathf.Max(480, height);
        Put(nameof(ScreenWidth), ScreenWidth);
        Put(nameof(ScreenHeight), ScreenHeight);
        ApplyScreen();
        Announce();
    }

    public static void SetQualityLevel(int level)
    {
        QualityLevel = Mathf.Clamp(level, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
        Put(nameof(QualityLevel), QualityLevel);
        ApplyQuality();
        Announce();
    }

    public static void SetShaders(ShaderPreset preset)
    {
        Shaders = preset;
        Put(nameof(Shaders), (int)preset);
        Announce();
    }

    public static void SetMotionBlur(bool value)
    {
        MotionBlur = value;
        Put(nameof(MotionBlur), value);
        Announce();
    }

    public static void SetPsxFilter(bool value)
    {
        PsxFilter = value;
        Put(nameof(PsxFilter), value);
        Announce();
    }

    public static void SetBelieveInBigfoot(bool value)
    {
        BelieveInBigfoot = value;
        Put(nameof(BelieveInBigfoot), value);
        Announce();
    }

    public static void SetMonkeyBusinessLevel(float value)
    {
        MonkeyBusinessLevel = value;
        Put(nameof(MonkeyBusinessLevel), value);
        Announce();
    }

    public static void SetCrosshairSize(float value)
    {
        CrosshairSize = Mathf.Clamp(value, 2f, 40f);
        Put(nameof(CrosshairSize), CrosshairSize);
        Announce();
    }

    public static void SetCrosshairThickness(float value)
    {
        CrosshairThickness = Mathf.Clamp(value, 1f, 10f);
        Put(nameof(CrosshairThickness), CrosshairThickness);
        Announce();
    }

    public static void SetCrosshairGap(float value)
    {
        CrosshairGap = Mathf.Clamp(value, 0f, 60f);
        Put(nameof(CrosshairGap), CrosshairGap);
        Announce();
    }

    public static void SetCrosshairDot(bool value)
    {
        CrosshairDot = value;
        Put(nameof(CrosshairDot), value);
        Announce();
    }

    public static void SetCrosshairOutline(bool value)
    {
        CrosshairOutline = value;
        Put(nameof(CrosshairOutline), value);
        Announce();
    }

    public static void SetCrosshairDynamic(bool value)
    {
        CrosshairDynamic = value;
        Put(nameof(CrosshairDynamic), value);
        Announce();
    }

    public static void SetCrosshairOverride(bool value)
    {
        CrosshairOverride = value;
        Put(nameof(CrosshairOverride), value);
        Announce();
    }

    public static void SetCrosshairColour(Color value)
    {
        CrosshairColour = value;
        Put("CrosshairR", value.r);
        Put("CrosshairG", value.g);
        Put("CrosshairB", value.b);
        Announce();
    }

    // ---------------------------------------------------------------- applying

    public static void ApplyAudio() => AudioListener.volume = MasterVolume;

    public static void ApplyScreen()
    {
        // Asking for the resolution you already have makes Windows flicker the window for no
        // reason, and on some drivers drops the graphics device entirely.
        if (Screen.width == ScreenWidth && Screen.height == ScreenHeight
            && Screen.fullScreen == Fullscreen)
            return;

        Screen.SetResolution(ScreenWidth, ScreenHeight,
                             Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
    }

    public static void ApplyQuality()
    {
        if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(QualityLevel, true);
    }

    /// Called on every spawn as well as on change, because the camera is created fresh each
    /// time you respawn and arrives holding the prefab's field of view rather than yours.
    public static void ApplyFov()
    {
        if (PlayerController.LocalCamera != null)
            PlayerController.LocalCamera.fieldOfView = Fov;
    }

    // ---------------------------------------------------------------- reset, per page and all

    // AimToggle was missing here for as long as it existed - saved and loaded, never reset.
    static readonly string[] AimKeys = { nameof(Sensitivity), nameof(AdsSensitivity), nameof(InvertY), nameof(AimToggle) };
    static readonly string[] AudioKeys = { nameof(MasterVolume), nameof(SfxVolume), nameof(MusicVolume) };

    static readonly string[] VideoKeys =
    {
        nameof(Fov), nameof(Fullscreen), nameof(ScreenWidth), nameof(ScreenHeight),
        nameof(QualityLevel), nameof(Shaders), nameof(MotionBlur), nameof(PsxFilter),
        nameof(BelieveInBigfoot), nameof(MonkeyBusinessLevel),
    };

    static readonly string[] CrosshairKeys =
    {
        nameof(CrosshairSize), nameof(CrosshairThickness), nameof(CrosshairGap),
        nameof(CrosshairDot), nameof(CrosshairOutline), nameof(CrosshairDynamic),
        nameof(CrosshairOverride), "CrosshairR", "CrosshairG", "CrosshairB",
    };

    static void DeleteKeys(string[] keys)
    {
        foreach (string key in keys)
            PlayerPrefs.DeleteKey(Prefix + key);
    }

    /// <summary>
    /// Per-page resets. "Reset to default (page specific)" - the settings screen used to have
    /// exactly one reset button and it always took everything, aim and keybinds included, which
    /// is a strange price to pay for wanting your crosshair colour back. Each of these only
    /// touches the keys that page itself can change, then reloads through <see cref="Load"/>
    /// rather than duplicating what each property's default is a second time.
    /// </summary>
    public static void ResetAim() { DeleteKeys(AimKeys); Load(); Announce(); }
    public static void ResetAudio() { DeleteKeys(AudioKeys); Load(); Announce(); }
    public static void ResetVideo() { DeleteKeys(VideoKeys); Load(); ApplyFov(); Announce(); }
    public static void ResetCrosshair() { DeleteKeys(CrosshairKeys); Load(); Announce(); }
    public static void ResetKeys() { KeyBinds.ResetAll(); Announce(); }

    /// <summary>
    /// Back to the defaults, for when somebody has made the game unplayable and can't remember
    /// which slider did it. The one case that still wants everything at once.
    /// </summary>
    public static void ResetAll()
    {
        DeleteKeys(AimKeys);
        DeleteKeys(AudioKeys);
        DeleteKeys(VideoKeys);
        DeleteKeys(CrosshairKeys);
        KeyBinds.ResetAll();
        Load();
        ApplyFov();
        Announce();
    }
}
