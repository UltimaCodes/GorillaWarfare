using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The settings screen. Opens anywhere with the menu key, applies everything live.
///
/// Built from four row templates rather than from twenty hand-placed controls. There are five
/// tabs and about two dozen settings between them, and authoring each one as its own object
/// would make a prefab nobody could find anything in - and every restyle would be twenty edits.
/// Instead the prefab carries one slider row, one toggle row, one choice row and one key row,
/// and the screen stamps out copies. Restyle the four and the whole screen follows.
///
/// Nothing here has an apply button. Every control writes straight through to GameSettings,
/// which saves and raises its change event, so the crosshair redraws while you drag the slider
/// and the shaders rebuild while you cycle the preset. You can't pick a good value for
/// something you can't see.
///
/// The one thing it does not do is pause. This is a multiplayer game and people are still
/// shooting at you while you fiddle with the sensitivity, which is a fair trade for being able
/// to change it mid-match at all.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    public static SettingsMenu Instance { get; private set; }

    /// Read by PlayerController, which must not grab the cursor back while a slider is being
    /// dragged, and must not fire the weapon through the panel.
    public static bool IsOpen { get; private set; }

    [Header("Frame")]
    [SerializeField] GameObject panel;
    [SerializeField] TMP_Text heading;
    [SerializeField] Button closeButton;
    [SerializeField] Button resetButton;

    [Header("Tabs")]
    [SerializeField] RectTransform tabBar;
    [SerializeField] Button tabTemplate;

    [Header("Rows")]
    [SerializeField] RectTransform content;
    [SerializeField] RectTransform sliderRow;
    [SerializeField] RectTransform toggleRow;
    [SerializeField] RectTransform choiceRow;
    [SerializeField] RectTransform bindRow;

    enum Tab { Aim, Audio, Video, Crosshair, Keys }

    Tab current = Tab.Aim;

    readonly List<GameObject> rows = new List<GameObject>();
    readonly List<Button> tabs = new List<Button>();

    /// Which action is waiting for a key press, or null. A rebind is modal by nature - the next
    /// key you touch is the answer, including keys that would otherwise do something else.
    KeyBinds.Action? listening;

    Resolution[] resolutions = Array.Empty<Resolution>();

    void Awake()
    {
        Instance = this;

        // Distinct resolutions only. Screen.resolutions lists every refresh rate separately, so
        // a monitor that does 1920x1080 at 60, 120 and 144 shows the same line three times.
        List<Resolution> distinct = new List<Resolution>();

        foreach (Resolution option in Screen.resolutions)
        {
            if (distinct.Count > 0
                && distinct[distinct.Count - 1].width == option.width
                && distinct[distinct.Count - 1].height == option.height)
                continue;

            distinct.Add(option);
        }

        resolutions = distinct.ToArray();

        HideTemplate(sliderRow);
        HideTemplate(toggleRow);
        HideTemplate(choiceRow);
        HideTemplate(bindRow);

        if (tabTemplate != null)
            tabTemplate.gameObject.SetActive(false);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(() =>
            {
                GameSettings.ResetAll();
                Show(current);
            });
        }

        BuildTabs();
        Hide();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        // Static, so it survives this object being destroyed by a scene change. Leaving it true
        // means the cursor never locks again and the game is unplayable.
        IsOpen = false;
    }

    static void HideTemplate(Component template)
    {
        if (template != null)
            template.gameObject.SetActive(false);
    }

    void Update()
    {
        if (listening.HasValue)
        {
            ListenForKey();
            return;
        }

        if (KeyBinds.Pressed(KeyBinds.Action.Menu))
        {
            if (IsOpen)
                Close();
            else
                Open();
        }
    }

    /// <summary>
    /// Waits for the next key and binds it.
    ///
    /// Reads the raw Input class rather than KeyBinds, because the whole point is to catch keys
    /// that are currently bound to something else. Escape cancels instead of binding - it's
    /// locked anyway, and somebody who opened a rebind by accident needs a way out that isn't
    /// giving up a key.
    /// </summary>
    void ListenForKey()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            listening = null;
            Show(current);
            return;
        }

        foreach (KeyCode key in AllKeys)
        {
            if (!Input.GetKeyDown(key))
                continue;

            KeyBinds.Set(listening.Value, key);
            listening = null;
            Show(current);
            return;
        }
    }

    /// <summary>
    /// Every key worth binding, cached once.
    ///
    /// Enum.GetValues on KeyCode returns over five hundred entries including joystick buttons
    /// that no keyboard can produce, and walking all of them every frame while a rebind is open
    /// is wasteful. This is the set a person can actually press.
    /// </summary>
    static readonly KeyCode[] AllKeys = BuildKeyList();

    static KeyCode[] BuildKeyList()
    {
        List<KeyCode> keys = new List<KeyCode>();

        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            // Joystick buttons are the bulk of the enum and none of them apply here.
            if (key.ToString().StartsWith("Joystick"))
                continue;

            if (key == KeyCode.None)
                continue;

            keys.Add(key);
        }

        return keys.ToArray();
    }

    public void Open()
    {
        IsOpen = true;

        if (panel != null)
            panel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Show(current);
    }

    public void Close()
    {
        listening = null;
        Hide();
    }

    void Hide()
    {
        IsOpen = false;

        if (panel != null)
            panel.SetActive(false);
    }

    // ---------------------------------------------------------------- tabs

    void BuildTabs()
    {
        if (tabBar == null || tabTemplate == null)
            return;

        foreach (Tab tab in Enum.GetValues(typeof(Tab)))
        {
            Tab captured = tab;

            Button button = Instantiate(tabTemplate, tabBar);
            button.name = $"Tab{tab}";
            button.gameObject.SetActive(true);

            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.text = tab.ToString().ToUpper();

            button.onClick.AddListener(() => Show(captured));
            tabs.Add(button);
        }
    }

    void Show(Tab tab)
    {
        current = tab;

        if (heading != null)
            heading.text = tab.ToString().ToUpper();

        for (int i = 0; i < tabs.Count; i++)
        {
            // The selected tab is drawn at full strength and the rest are dimmed, rather than
            // using a separate highlight object that would have to be styled twice.
            TMP_Text label = tabs[i].GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.alpha = i == (int)tab ? 1f : 0.45f;
        }

        Clear();

        switch (tab)
        {
            case Tab.Aim: BuildAim(); break;
            case Tab.Audio: BuildAudio(); break;
            case Tab.Video: BuildVideo(); break;
            case Tab.Crosshair: BuildCrosshair(); break;
            case Tab.Keys: BuildKeys(); break;
        }
    }

    void Clear()
    {
        foreach (GameObject row in rows)
            Destroy(row);

        rows.Clear();
    }

    // ---------------------------------------------------------------- the tabs themselves

    void BuildAim()
    {
        Slider("sensitivity", GameSettings.Sensitivity,
               GameSettings.MinSensitivity, GameSettings.MaxSensitivity,
               GameSettings.SetSensitivity, "F2");

        Slider("scoped sensitivity", GameSettings.AdsSensitivity, 0.1f, 2f,
               GameSettings.SetAdsSensitivity, "F2");

        Toggle("invert vertical", GameSettings.InvertY, GameSettings.SetInvertY);
    }

    void BuildAudio()
    {
        Slider("master", GameSettings.MasterVolume, 0f, 1f, GameSettings.SetMasterVolume, "P0");
        Slider("effects", GameSettings.SfxVolume, 0f, 1f, GameSettings.SetSfxVolume, "P0");
        Slider("music", GameSettings.MusicVolume, 0f, 1f, GameSettings.SetMusicVolume, "P0");
    }

    void BuildVideo()
    {
        Slider("field of view", GameSettings.Fov, GameSettings.MinFov, GameSettings.MaxFov,
               GameSettings.SetFov, "F0");

        Toggle("fullscreen", GameSettings.Fullscreen, GameSettings.SetFullscreen);

        // Resolutions listed largest first, because that is the order people look for their own.
        string[] names = new string[resolutions.Length];
        int currentResolution = 0;

        for (int i = 0; i < resolutions.Length; i++)
        {
            names[i] = $"{resolutions[i].width} x {resolutions[i].height}";

            if (resolutions[i].width == GameSettings.ScreenWidth
                && resolutions[i].height == GameSettings.ScreenHeight)
                currentResolution = i;
        }

        if (names.Length > 0)
        {
            Choice("resolution", names, currentResolution,
                   i => GameSettings.SetResolution(resolutions[i].width, resolutions[i].height));
        }

        Choice("detail", QualitySettings.names, Mathf.Max(0, GameSettings.QualityLevel),
               GameSettings.SetQualityLevel);

        Choice("shaders", Enum.GetNames(typeof(GameSettings.ShaderPreset)), (int)GameSettings.Shaders,
               i => GameSettings.SetShaders((GameSettings.ShaderPreset)i));

        Toggle("motion blur", GameSettings.MotionBlur, GameSettings.SetMotionBlur);
    }

    void BuildCrosshair()
    {
        Slider("size", GameSettings.CrosshairSize, 2f, 40f, GameSettings.SetCrosshairSize, "F0");
        Slider("thickness", GameSettings.CrosshairThickness, 1f, 10f,
               GameSettings.SetCrosshairThickness, "F0");
        Slider("gap", GameSettings.CrosshairGap, 0f, 60f, GameSettings.SetCrosshairGap, "F0");

        Toggle("centre dot", GameSettings.CrosshairDot, GameSettings.SetCrosshairDot);
        Toggle("outline", GameSettings.CrosshairOutline, GameSettings.SetCrosshairOutline);
        Toggle("opens with spread", GameSettings.CrosshairDynamic, GameSettings.SetCrosshairDynamic);

        Color colour = GameSettings.CrosshairColour;

        Slider("red", colour.r, 0f, 1f,
               v => GameSettings.SetCrosshairColour(
                   new Color(v, GameSettings.CrosshairColour.g, GameSettings.CrosshairColour.b)), "F2");

        Slider("green", colour.g, 0f, 1f,
               v => GameSettings.SetCrosshairColour(
                   new Color(GameSettings.CrosshairColour.r, v, GameSettings.CrosshairColour.b)), "F2");

        Slider("blue", colour.b, 0f, 1f,
               v => GameSettings.SetCrosshairColour(
                   new Color(GameSettings.CrosshairColour.r, GameSettings.CrosshairColour.g, v)), "F2");
    }

    void BuildKeys()
    {
        foreach (KeyBinds.Action action in Enum.GetValues(typeof(KeyBinds.Action)))
        {
            if (KeyBinds.IsLocked(action))
                continue;

            Bind(action);
        }
    }

    // ---------------------------------------------------------------- row factories

    RectTransform Stamp(RectTransform template)
    {
        RectTransform row = Instantiate(template, content);
        row.gameObject.SetActive(true);
        rows.Add(row.gameObject);

        return row;
    }

    /// <summary>
    /// Finds a named child in a row template.
    ///
    /// By name rather than by index, so rearranging a template in the editor - which is exactly
    /// what these are for - doesn't silently rewire which control does what.
    /// </summary>
    static T Part<T>(RectTransform row, string childName) where T : Component
    {
        Transform child = row.Find(childName);
        return child != null ? child.GetComponent<T>() : row.GetComponentInChildren<T>(true);
    }

    void Slider(string label, float value, float min, float max, Action<float> set, string format)
    {
        RectTransform row = Stamp(sliderRow);
        row.name = $"Row {label}";

        TMP_Text name = Part<TMP_Text>(row, "Label");
        if (name != null)
            name.text = label;

        TMP_Text readout = Part<TMP_Text>(row, "Value");
        Slider slider = row.GetComponentInChildren<Slider>(true);

        if (slider == null)
            return;

        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);

        if (readout != null)
            readout.text = Format(value, format);

        slider.onValueChanged.AddListener(v =>
        {
            set(v);

            if (readout != null)
                readout.text = Format(v, format);
        });
    }

    static string Format(float value, string format) =>
        format == "P0" ? Mathf.RoundToInt(value * 100f) + "%" : value.ToString(format);

    void Toggle(string label, bool value, Action<bool> set)
    {
        RectTransform row = Stamp(toggleRow);
        row.name = $"Row {label}";

        TMP_Text name = Part<TMP_Text>(row, "Label");
        if (name != null)
            name.text = label;

        Toggle toggle = row.GetComponentInChildren<Toggle>(true);
        TMP_Text readout = Part<TMP_Text>(row, "Value");

        if (toggle == null)
            return;

        toggle.SetIsOnWithoutNotify(value);

        if (readout != null)
            readout.text = value ? "ON" : "OFF";

        toggle.onValueChanged.AddListener(v =>
        {
            set(v);

            if (readout != null)
                readout.text = v ? "ON" : "OFF";
        });
    }

    void Choice(string label, string[] options, int index, Action<int> set)
    {
        RectTransform row = Stamp(choiceRow);
        row.name = $"Row {label}";

        TMP_Text name = Part<TMP_Text>(row, "Label");
        if (name != null)
            name.text = label;

        TMP_Text readout = Part<TMP_Text>(row, "Value");
        Button previous = Part<Button>(row, "Previous");
        Button next = Part<Button>(row, "Next");

        int at = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));

        void Draw()
        {
            if (readout != null && options.Length > 0)
                readout.text = options[at].ToUpper();
        }

        Draw();

        // Wraps rather than stopping at the ends. With three shader presets, being unable to go
        // back round from the first is just an extra click.
        if (previous != null)
        {
            previous.onClick.AddListener(() =>
            {
                at = at <= 0 ? options.Length - 1 : at - 1;
                set(at);
                Draw();
            });
        }

        if (next != null)
        {
            next.onClick.AddListener(() =>
            {
                at = at >= options.Length - 1 ? 0 : at + 1;
                set(at);
                Draw();
            });
        }
    }

    void Bind(KeyBinds.Action action)
    {
        RectTransform row = Stamp(bindRow);
        row.name = $"Row {action}";

        TMP_Text name = Part<TMP_Text>(row, "Label");
        if (name != null)
            name.text = KeyBinds.Describe(action);

        Button button = row.GetComponentInChildren<Button>(true);
        TMP_Text readout = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;

        if (readout != null)
            readout.text = KeyBinds.Label(action);

        if (button == null)
            return;

        button.onClick.AddListener(() =>
        {
            listening = action;

            if (readout != null)
                readout.text = "PRESS A KEY";
        });
    }
}
