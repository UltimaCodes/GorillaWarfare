using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the lobby colour picker as real objects, once.
//
// Same shape as ModeSelectorBuilder and for the same reason: it goes into the scene so it can be
// moved and restyled, and it is built from one swatch template so restyling one restyles eight.
//
// It deliberately sits under the mode selector rather than anywhere clever. The numbers here are
// a starting position, not a design.
public static class ColourPickerBuilder
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string RootName = "ColourPicker";

    const float SwatchSize = 58f;
    const float Spacing = 10f;

    /// Swatches across before wrapping. Eight is what fitted the panel when there were eight
    /// colours, and it is still the right width now that there are twelve.
    const int PerRow = 8;

    [MenuItem("Tools/Gorilla Warfare/Build the colour picker")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform lobby = Find(scene, "RoomMenu");

        if (lobby == null)
        {
            Debug.LogError("[colour] no RoomMenu in the menu scene");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        Transform existing = lobby.Find(RootName);
        if (existing != null)
        {
            Debug.Log("[colour] replacing the previous picker");
            Object.DestroyImmediate(existing.gameObject);
        }

        TMP_FontAsset font = FindFont();

        GameObject root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(lobby, false);

        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
        rootRect.pivot = new Vector2(0f, 1f);
        rootRect.anchoredPosition = new Vector2(60f, -900f);
        rootRect.sizeDelta = new Vector2(PerRow * (SwatchSize + Spacing),
                                         140f + Mathf.Max(0, Mathf.CeilToInt(
                                             PlayerColours.Palette.Length / (float)PerRow) - 1)
                                         * (SwatchSize + Spacing));

        TMP_Text caption = Text(root.transform, "Caption", font, 20f,
                                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(400f, 30f));
        caption.text = "BANANA";
        caption.color = new Color(1f, 1f, 1f, 0.7f);

        // The swatches themselves, laid out by a horizontal group so adding a colour to the
        // palette needs no arithmetic here.
        GameObject row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(root.transform, false);

        RectTransform rowRect = (RectTransform)row.transform;
        rowRect.anchorMin = rowRect.anchorMax = new Vector2(0f, 1f);
        rowRect.pivot = new Vector2(0f, 1f);
        rowRect.anchoredPosition = new Vector2(0f, -38f);
        // Tall enough for however many rows the palette needs.
        int rows = Mathf.CeilToInt(PlayerColours.Palette.Length / (float)PerRow);
        rowRect.sizeDelta = new Vector2(PerRow * (SwatchSize + Spacing),
                                        rows * (SwatchSize + Spacing));

        // A grid rather than a row. Eight swatches fitted across the panel; twelve did not, and
        // the last four went off the edge of the screen where nobody could click them. A grid
        // wraps on its own, so the palette can grow again without this needing to know.
        GridLayoutGroup layout = row.AddComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(SwatchSize, SwatchSize);
        layout.spacing = new Vector2(Spacing, Spacing);
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;

        // Fixed at eight across, so the wrap point is a decision rather than whatever the panel
        // width happens to be this frame - and eight is what already fitted.
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = PerRow;

        GameObject swatchObject = new GameObject("SwatchTemplate",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        swatchObject.transform.SetParent(row.transform, false);

        RectTransform swatchRect = (RectTransform)swatchObject.transform;
        swatchRect.sizeDelta = new Vector2(SwatchSize, SwatchSize);

        Image face = swatchObject.GetComponent<Image>();
        face.color = Color.white;

        Button button = swatchObject.GetComponent<Button>();
        button.targetGraphic = face;

        // No colour tint on the button itself - the swatch's own colour is the entire content,
        // and Unity's default tint would wash it out on hover and make two colours look alike.
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = Color.white;
        colours.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colours.selectedColor = Color.white;
        button.colors = colours;

        swatchObject.SetActive(false);

        ColourPicker picker = root.AddComponent<ColourPicker>();

        SerializedObject so = new SerializedObject(picker);
        so.FindProperty("row").objectReferenceValue = rowRect;
        so.FindProperty("swatchTemplate").objectReferenceValue = button;
        so.FindProperty("caption").objectReferenceValue = caption;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[colour] built under RoomMenu with {PlayerColours.Palette.Length} swatches - "
                  + "move and restyle it from here");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, float size,
                         Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        if (font != null)
            text.font = font;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        return text;
    }

    /// Whatever the lobby already uses, so this doesn't arrive in a different typeface than
    /// everything around it.
    static TMP_FontAsset FindFont()
    {
        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
        {
            if (text.font != null)
                return text.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    static Transform Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
        }

        return null;
    }
}
