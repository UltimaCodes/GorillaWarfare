using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu's look, for the screens built as prefabs outside it - the inventory and the
/// crates. The same fonts and ink presets (MenuBuilder.InkPreset), the same palette, the same two
/// kinds of button: words that light up banana with a bar beside them, and the one filled banana
/// button per screen. Copied from MenuBuilder's own helpers rather than calling them, because
/// MenuBuilder's builds the menu and is not to be run again (see its header).
/// </summary>
public static class MenuStyle
{
    public static readonly Color Ink = new Color(0.07f, 0.08f, 0.1f);
    public static readonly Color Banana = new Color(1f, 0.82f, 0.12f);
    public static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
    public static readonly Color Danger = new Color(1f, 0.1f, 0.25f);
    public static readonly Color PanelColour = new Color(0.035f, 0.04f, 0.05f, 0.86f);
    public static readonly Color Backing = new Color(0.03f, 0.035f, 0.045f, 1f);
    public static readonly Color Rule = new Color(1f, 1f, 1f, 0.12f);
    public static readonly Color Card = new Color(0.07f, 0.08f, 0.1f, 0.92f);

    public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    public static readonly Vector2 TopRight = new Vector2(1f, 1f);
    public static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    public static readonly Vector2 BottomRight = new Vector2(1f, 0f);
    public static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);
    public static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    public static readonly Vector2 TopMiddle = new Vector2(0.5f, 1f);
    public static readonly Vector2 BottomMiddle = new Vector2(0.5f, 0f);

    public static TMP_FontAsset Display { get; private set; }
    public static TMP_FontAsset Body { get; private set; }
    public static Material DisplayInk { get; private set; }
    public static Material BodyInk { get; private set; }
    public static Material DisplayFlat { get; private set; }

    public static void Load()
    {
        Display = FindFont("Anton");
        Body = FindFont("Jersey10");
        DisplayInk = MenuBuilder.InkPreset(Display);
        BodyInk = MenuBuilder.InkPreset(Body);
        DisplayFlat = Display.material;
    }

    /// A screen-space overlay canvas root with its scaler set the way the menu's is.
    public static GameObject CanvasRoot(string name, int sortingOrder)
    {
        GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return root;
    }

    public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                     Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    public static RectTransform Stretch(Transform parent, string name)
    {
        RectTransform rect = Rect(parent, name, Vector2.zero, Vector2.one, Middle, Vector2.zero, Vector2.zero);
        return rect;
    }

    public static Image Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                              Vector2 position, Vector2 size, Color colour, bool raycast = false)
    {
        RectTransform rect = Rect(parent, name, anchorMin, anchorMax, pivot, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = raycast;
        return image;
    }

    public static TMP_Text Label(Transform parent, string name, string text, bool display, float size, Color colour,
                                 TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = display ? Display : Body;
        label.fontSharedMaterial = display ? DisplayInk : BodyInk;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    /// Wraps, for a paragraph.
    public static TMP_Text Paragraph(Transform parent, string name, string text, float size, Color colour,
                                     TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        TMP_Text label = Label(parent, name, text, false, size, colour, alignment, anchor, position, dimensions);
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    /// The words-only button - banana and a bar beside it on hover (MenuButton), like the menu's.
    public static Button TextButton(Transform parent, string name, string text, float size, Vector2 anchor,
                                    Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);

        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        Image accent = Panel(rect, "Accent", MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(-20f, 0f),
                             new Vector2(7f, size * 0.72f), new Color(Banana.r, Banana.g, Banana.b, 0f));

        TMP_Text label = Label(rect, "Label", text, true, size, Color.white, TextAlignmentOptions.MidlineLeft,
                               MiddleLeft, Vector2.zero, dimensions);
        label.rectTransform.pivot = MiddleLeft;

        MenuButton look = rect.gameObject.AddComponent<MenuButton>();
        SerializedObject so = new SerializedObject(look);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("accent").objectReferenceValue = accent;
        so.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    /// The one filled button per screen - banana, dark Anton - with a hover lift.
    public static Button PrimaryButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 dimensions,
                                       Color? face = null, Color? ink = null)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);

        Image image = rect.gameObject.AddComponent<Image>();
        image.color = face ?? Banana;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colours = button.colors;
        colours.normalColor = new Color(0.9f, 0.9f, 0.9f);
        colours.highlightedColor = Color.white;
        colours.selectedColor = new Color(0.9f, 0.9f, 0.9f);
        colours.pressedColor = new Color(0.72f, 0.72f, 0.72f);
        colours.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        colours.colorMultiplier = 1.1f;
        colours.fadeDuration = 0.08f;
        button.colors = colours;

        TMP_Text label = Label(rect, "Label", text, true, dimensions.y * 0.46f, ink ?? Ink, TextAlignmentOptions.Center,
                               Middle, Vector2.zero, dimensions);
        label.fontSharedMaterial = DisplayFlat;

        rect.gameObject.AddComponent<HoverLift>();
        return button;
    }

    /// A dark button with a thin outline - for everything that isn't the screen's main thing.
    public static Button GhostButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 dimensions,
                                     Color? edge = null)
    {
        Button button = PrimaryButton(parent, name, text, anchor, position, dimensions, new Color(0.1f, 0.11f, 0.14f, 0.95f), Color.white);
        Outline outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = edge ?? new Color(1f, 1f, 1f, 0.25f);
        outline.effectDistance = new Vector2(2f, -2f);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>();
        label.fontSharedMaterial = DisplayInk;
        return button;
    }

    /// A thin accent bar down one edge.
    public static Image Bar(RectTransform card, Color colour, bool left, float width = 6f)
    {
        Vector2 edge = left ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        return Panel(card, "AccentBar", new Vector2(edge.x, 0f), new Vector2(edge.x, 1f), edge, Vector2.zero, new Vector2(width, 0f), colour);
    }

    /// A scrolling grid of cards, vertical - more than fit just scroll.
    public static RectTransform ScrollGrid(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 dimensions,
                                           Vector2 cell, Vector2 spacing, int columns)
    {
        RectTransform root = Rect(parent, name, anchor, anchor, anchor, position, dimensions);
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        RectTransform viewport = Rect(root, "Viewport", Vector2.zero, Vector2.one, TopLeft, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cell;
        grid.spacing = spacing;
        grid.padding = new RectOffset(12, 12, 12, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;
        return content;
    }

    public static Sprite Sprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"[menu style] no sprite at {path}");
        return sprite;
    }

    public static void Wire(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        if (property == null)
        {
            Debug.LogError($"[menu style] {target.GetType().Name} has no field '{field}'");
            return;
        }

        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void Wire(Object target, string field, Object[] values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty list = so.FindProperty(field);
        if (list == null)
        {
            Debug.LogError($"[menu style] {target.GetType().Name} has no field '{field}'");
            return;
        }

        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static TMP_FontAsset FindFont(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains(name) && path.EndsWith("SDF.asset"))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }
}
