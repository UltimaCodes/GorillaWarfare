using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Builds the settings screen as a prefab, once.
//
// A prefab rather than objects in a scene, because settings have to be reachable from the menu
// and from the middle of a match, and those are two different scenes. One asset, instantiated
// onto the object that survives the trip between them.
//
// The previous attempt at a settings menu was built entirely from code at runtime and Ryaan
// reverted it, for the same reason he asked for the mode selector and the HUD as real objects:
// a panel that only exists while the game is running is a panel nobody can move. This builds
// the thing once and then gets out of the way. Open the prefab, drag it about, change the
// colours, change the font.
//
// Only four rows are authored - a slider, a toggle, a choice and a key binding. The screen
// stamps copies of them at runtime, so restyling those four restyles all two dozen settings.
public static class SettingsMenuBuilder
{
    const string Folder = "Assets/Resources";
    const string Path = Folder + "/SettingsMenu.prefab";

    static readonly Color Ink = new Color(0.94f, 0.94f, 0.9f);
    static readonly Color Dim = new Color(0.94f, 0.94f, 0.9f, 0.5f);
    static readonly Color Backdrop = new Color(0.03f, 0.03f, 0.04f, 0.93f);
    static readonly Color Face = new Color(0.11f, 0.11f, 0.13f, 1f);
    static readonly Color Accent = new Color(1f, 0.42f, 0.06f);

    const float RowHeight = 46f;
    const float PanelWidth = 900f;

    [MenuItem("Tools/Gorilla Warfare/Build the settings menu")]
    public static void Run()
    {
        TMP_FontAsset font = FindFont();

        if (!Directory.Exists(Folder))
            Directory.CreateDirectory(Folder);

        // ---------------------------------------------------------------- canvas
        GameObject root = new GameObject("SettingsMenu",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        // Above everything. This is the one screen that has to be on top of the HUD, the
        // scoreboard and whatever the menu is doing.
        canvas.sortingOrder = 500;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        SettingsMenu menu = root.AddComponent<SettingsMenu>();

        // ---------------------------------------------------------------- panel
        // Full screen dimmer first, so the game behind is still visible but obviously not
        // where your attention is. It also swallows clicks, which stops you shooting through
        // the settings screen.
        GameObject panel = Box(root.transform, "Panel", Vector2.zero, Vector2.one, Vector2.zero,
                               Vector2.zero, Backdrop);

        GameObject frame = Box(panel.transform, "Frame", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               Vector2.zero, new Vector2(PanelWidth, 780f), Face);

        TMP_Text heading = Label(frame.transform, "Heading", font, 46f, TextAlignmentOptions.Left,
                                 new Vector2(0f, 1f), new Vector2(40f, -34f), new Vector2(500f, 56f));
        heading.text = "AIM";
        heading.color = Accent;

        // "BACK" rather than "CLOSE": opening this from the main menu makes it a screen you
        // came from somewhere to reach, and back is what you want from it.
        Button close = Push(frame.transform, "Close", font, "BACK",
                            new Vector2(1f, 1f), new Vector2(-40f, -34f), new Vector2(160f, 48f));

        Button reset = Push(frame.transform, "Reset", font, "DEFAULTS",
                            new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(210f, 48f));

        // Bottom left, well away from DEFAULTS - the two most destructive buttons on the screen
        // should not be neighbours. Hidden unless there is a match to leave.
        Button quit = Push(frame.transform, "Quit", font, "MAIN MENU",
                           new Vector2(0f, 0f), new Vector2(40f, 34f), new Vector2(260f, 48f));
        quit.gameObject.SetActive(false);

        // Same corner as MAIN MENU, because the two are never up at once - one is menu only and
        // the other match only.
        Button sandbox = Push(frame.transform, "Sandbox", font, "SANDBOX",
                              new Vector2(0f, 0f), new Vector2(40f, 34f), new Vector2(260f, 48f));
        sandbox.gameObject.SetActive(false);

        // ---------------------------------------------------------------- tabs
        GameObject tabBar = Empty(frame.transform, "Tabs", new Vector2(0f, 1f),
                                  new Vector2(40f, -104f), new Vector2(PanelWidth - 80f, 54f));

        HorizontalLayoutGroup tabLayout = tabBar.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 8f;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;

        Button tabTemplate = Push(tabBar.transform, "TabTemplate", font, "TAB",
                                  new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160f, 54f));
        tabTemplate.gameObject.SetActive(false);

        // ---------------------------------------------------------------- scrolling content
        // A scroll view because the keys tab has twelve rows and the panel is a fixed height.
        // Without it the last few bindings would sit below the bottom edge with no way to reach
        // them, which is a good way to lose the reload key forever.
        GameObject viewport = Box(frame.transform, "Viewport", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                  new Vector2(40f, -172f), new Vector2(PanelWidth - 80f, 520f),
                                  new Color(0f, 0f, 0f, 0.25f));

        RectTransform viewportRect = (RectTransform)viewport.transform;
        viewportRect.pivot = new Vector2(0f, 1f);

        viewport.AddComponent<RectMask2D>();

        GameObject content = Empty(viewport.transform, "Content", new Vector2(0f, 1f),
                                   Vector2.zero, new Vector2(PanelWidth - 80f, 520f));

        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.pivot = new Vector2(0f, 1f);

        VerticalLayoutGroup rowLayout = content.AddComponent<VerticalLayoutGroup>();
        rowLayout.spacing = 6f;
        rowLayout.padding = new RectOffset(14, 14, 14, 14);
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.AddComponent<ScrollRect>();
        scroll.content = contentRect;
        scroll.viewport = viewportRect;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        // ---------------------------------------------------------------- row templates
        RectTransform sliderRow = SliderRow(content.transform, font);
        RectTransform toggleRow = ToggleRow(content.transform, font);
        RectTransform choiceRow = ChoiceRow(content.transform, font);
        RectTransform bindRow = BindRow(content.transform, font);

        // ---------------------------------------------------------------- wiring
        SerializedObject so = new SerializedObject(menu);

        Wire(so, "panel", panel);
        Wire(so, "heading", heading);
        Wire(so, "closeButton", close);
        Wire(so, "resetButton", reset);
        Wire(so, "quitButton", quit);
        Wire(so, "sandboxButton", sandbox);
        Wire(so, "tabBar", (RectTransform)tabBar.transform);
        Wire(so, "tabTemplate", tabTemplate);
        Wire(so, "content", contentRect);
        Wire(so, "sliderRow", sliderRow);
        Wire(so, "toggleRow", toggleRow);
        Wire(so, "choiceRow", choiceRow);
        Wire(so, "bindRow", bindRow);

        so.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, Path);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[settings] built {Path} with font '{(font != null ? font.name : "none")}' - "
                  + "open the prefab to restyle it, the four row templates drive everything");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Wire(SerializedObject so, string field, Object value)
    {
        SerializedProperty property = so.FindProperty(field);

        if (property == null)
        {
            Debug.LogError($"[settings] SettingsMenu has no field called '{field}'");
            return;
        }

        property.objectReferenceValue = value;
    }

    // ---------------------------------------------------------------- rows

    static RectTransform Row(Transform parent, string name, TMP_FontAsset font, out TMP_Text label)
    {
        GameObject row = Box(parent, name, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero,
                             new Vector2(0f, RowHeight), new Color(1f, 1f, 1f, 0.04f));

        LayoutElement element = row.AddComponent<LayoutElement>();
        element.preferredHeight = RowHeight;
        element.minHeight = RowHeight;

        label = Label(row.transform, "Label", font, 26f, TextAlignmentOptions.Left,
                      new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(LabelWidth, RowHeight));
        label.color = Ink;

        row.SetActive(false);
        return (RectTransform)row.transform;
    }

    // Three columns, measured from the right edge of the row so they cannot drift into each
    // other when the panel is resized.
    //
    // The first version anchored the slider right with a centre pivot, which means the position
    // names the middle of a 360 wide control - so it reached 180 past its anchor, straight under
    // the value text and 30 pixels off the edge of the panel. Everything here is right-pivoted
    // now, so the number is the edge and the columns are exact.
    const float ValueRight = -16f;      // value text runs -16 to -126
    const float ValueWidth = 110f;
    const float ControlRight = -140f;   // controls stop here, clear of the value
    const float SliderWidth = 300f;
    const float LabelWidth = 330f;

    static readonly Vector2 RightMiddle = new Vector2(1f, 0.5f);

    static RectTransform SliderRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform row = Row(parent, "SliderRowTemplate", font, out TMP_Text _);

        // The slider proper. uGUI needs the whole hierarchy - a background, a fill inside a fill
        // area, and a handle inside a slide area - and none of it is built for you.
        GameObject slider = Box(row, "Slider", RightMiddle, RightMiddle, RightMiddle,
                                new Vector2(ControlRight, 0f), new Vector2(SliderWidth, 12f),
                                new Color(1f, 1f, 1f, 0.12f));

        GameObject fillArea = Empty(slider.transform, "Fill Area", new Vector2(0.5f, 0.5f),
                                    Vector2.zero, Vector2.zero);
        Stretch((RectTransform)fillArea.transform);

        GameObject fill = Box(fillArea.transform, "Fill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                              Vector2.zero, new Vector2(10f, 12f), Accent);
        Stretch((RectTransform)fill.transform);

        GameObject slideArea = Empty(slider.transform, "Handle Slide Area", new Vector2(0.5f, 0.5f),
                                     Vector2.zero, Vector2.zero);
        Stretch((RectTransform)slideArea.transform);

        GameObject handle = Box(slideArea.transform, "Handle", new Vector2(0.5f, 0.5f),
                                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 30f), Ink);

        Slider control = slider.AddComponent<Slider>();
        control.fillRect = (RectTransform)fill.transform;
        control.handleRect = (RectTransform)handle.transform;
        control.targetGraphic = handle.GetComponent<Image>();
        control.direction = Slider.Direction.LeftToRight;

        TMP_Text value = Label(row, "Value", font, 26f, TextAlignmentOptions.Right,
                               RightMiddle, new Vector2(ValueRight, 0f),
                               new Vector2(ValueWidth, RowHeight));
        value.color = Accent;

        return row;
    }

    static RectTransform ToggleRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform row = Row(parent, "ToggleRowTemplate", font, out TMP_Text _);

        GameObject box = Box(row, "Toggle", RightMiddle, RightMiddle, RightMiddle,
                             new Vector2(ControlRight, 0f), new Vector2(30f, 30f),
                             new Color(1f, 1f, 1f, 0.12f));

        GameObject tick = Box(box.transform, "Checkmark", new Vector2(0.5f, 0.5f),
                              new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f), Accent);

        Toggle control = box.AddComponent<Toggle>();
        control.targetGraphic = box.GetComponent<Image>();
        control.graphic = tick.GetComponent<Image>();

        TMP_Text value = Label(row, "Value", font, 26f, TextAlignmentOptions.Right,
                               RightMiddle, new Vector2(ValueRight, 0f),
                               new Vector2(ValueWidth, RowHeight));
        value.color = Accent;

        return row;
    }

    static RectTransform ChoiceRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform row = Row(parent, "ChoiceRowTemplate", font, out TMP_Text _);

        // Arrows either side of the reading, all right-pivoted so the three tile exactly: next
        // ends at -16, the value runs -70 to -270, previous ends at -280.
        Push(row, "Next", font, ">", RightMiddle, new Vector2(-16f, 0f), new Vector2(44f, 36f));

        TMP_Text value = Label(row, "Value", font, 24f, TextAlignmentOptions.Center,
                               RightMiddle, new Vector2(-70f, 0f), new Vector2(200f, RowHeight));
        value.color = Accent;

        Push(row, "Previous", font, "<", RightMiddle, new Vector2(-280f, 0f), new Vector2(44f, 36f));

        return row;
    }

    static RectTransform BindRow(Transform parent, TMP_FontAsset font)
    {
        RectTransform row = Row(parent, "BindRowTemplate", font, out TMP_Text _);

        Push(row, "Bind", font, "KEY", RightMiddle, new Vector2(ValueRight, 0f),
             new Vector2(260f, 36f));

        return row;
    }

    // ---------------------------------------------------------------- primitives

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static GameObject Empty(Transform parent, string name, Vector2 anchor, Vector2 position,
                            Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        return go;
    }

    static GameObject Box(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 position, Vector2 size, Color colour)
    {
        return Box(parent, name, anchorMin, anchorMax, new Vector2(0.5f, 0.5f), position, size, colour);
    }

    static GameObject Box(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
                          Vector2 pivot, Vector2 position, Vector2 size, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        // A full-stretch box has its size driven by the anchors, so sizeDelta has to be zeroed
        // or it grows past its parent by whatever was passed in.
        if (anchorMin == Vector2.zero && anchorMax == Vector2.one)
            rect.sizeDelta = Vector2.zero;

        go.GetComponent<Image>().color = colour;
        return go;
    }

    static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, float size,
                          TextAlignmentOptions alignment, Vector2 anchor, Vector2 position,
                          Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;
        text.color = Ink;

        if (font != null)
            text.font = font;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        return text;
    }

    static Button Push(Transform parent, string name, TMP_FontAsset font, string caption,
                       Vector2 anchor, Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                                       typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image face = go.GetComponent<Image>();
        face.color = new Color(1f, 1f, 1f, 0.1f);

        Button button = go.GetComponent<Button>();
        button.targetGraphic = face;

        ColorBlock colours = button.colors;
        colours.highlightedColor = new Color(1f, 0.42f, 0.06f, 0.35f);
        colours.pressedColor = Accent;
        button.colors = colours;

        TMP_Text label = Label(go.transform, "Label", font, 24f, TextAlignmentOptions.Center,
                               new Vector2(0.5f, 0.5f), Vector2.zero, size);
        label.text = caption;

        return button;
    }

    /// The in-game font, since this screen opens over the game as often as over the menu.
    static TMP_FontAsset FindFont()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (path.Contains("Helvetica Punk"))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }
}
