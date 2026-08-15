using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the mode selector into the lobby as real objects, once.
//
// This is a one-shot: it creates the hierarchy, wires the references and saves the scene. After
// that the scene owns it - move it, restyle it, change the font, delete bits of it. Running it
// again replaces what it made and nothing else, so a botched experiment can be undone without
// hand-editing scene YAML.
//
// It deliberately doesn't lay anything out cleverly. The numbers here are a starting position
// under the existing buttons, matched to their width and height so it doesn't look out of
// place; the point of the exercise is that they're now numbers in a scene rather than numbers
// in a source file.
public static class ModeSelectorBuilder
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string RootName = "ModeSelector";

    // Matches the existing buttons, so it sits with them rather than beside them.
    const float ButtonWidth = 600f;
    const float ButtonHeight = 70f;

    [MenuItem("Tools/Gorilla Warfare/Build the mode selector")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform lobby = Find(scene, "RoomMenu");
        if (lobby == null)
        {
            Debug.LogError("[mode] no RoomMenu in the menu scene - nothing to build into");
            EditorApplication.Exit(1);
            return;
        }

        // Replace rather than duplicate.
        Transform existing = lobby.Find(RootName);
        if (existing != null)
        {
            Debug.Log("[mode] replacing the previous selector");
            Object.DestroyImmediate(existing.gameObject);
        }

        TMP_FontAsset font = FindFont();

        GameObject root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(lobby, false);

        RectTransform rootRect = (RectTransform)root.transform;
        Anchor(rootRect, new Vector2(0f, 1f), new Vector2(60f, -690f), new Vector2(ButtonWidth, 190f));

        // Caption above.
        TMP_Text caption = MakeText(root.transform, "Caption", font, 20f, TextAlignmentOptions.BottomLeft,
                                    new Vector2(0f, 60f), new Vector2(ButtonWidth, 30f));
        caption.text = "PLAYING";
        caption.color = new Color(1f, 1f, 1f, 0.7f);

        // The button itself.
        GameObject buttonObject = new GameObject("CycleButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(root.transform, false);

        Anchor((RectTransform)buttonObject.transform, new Vector2(0.5f, 0.5f),
               new Vector2(ButtonWidth * 0.5f, 0f), new Vector2(ButtonWidth, ButtonHeight));

        Image face = buttonObject.GetComponent<Image>();
        face.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = face;

        TMP_Text label = MakeText(buttonObject.transform, "Label", font, 30f, TextAlignmentOptions.Center,
                                  Vector2.zero, new Vector2(ButtonWidth, ButtonHeight));
        label.text = "DEATHMATCH";

        // Shown to everyone who isn't the host, in the button's place.
        TMP_Text readout = MakeText(root.transform, "Readout", font, 30f, TextAlignmentOptions.Left,
                                    new Vector2(0f, 0f), new Vector2(ButtonWidth, ButtonHeight));
        readout.text = "DEATHMATCH";
        readout.color = new Color(1f, 0.85f, 0.2f);
        readout.gameObject.SetActive(false);

        // One line under it saying what the mode does.
        TMP_Text description = MakeText(root.transform, "Description", font, 17f, TextAlignmentOptions.TopLeft,
                                        new Vector2(0f, -56f), new Vector2(ButtonWidth, 40f));
        description.text = "three random bananas, most kills on the clock";
        description.color = new Color(1f, 1f, 1f, 0.5f);

        ModeSelector selector = root.AddComponent<ModeSelector>();

        SerializedObject so = new SerializedObject(selector);
        so.FindProperty("button").objectReferenceValue = button;
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("description").objectReferenceValue = description;
        so.FindProperty("readout").objectReferenceValue = readout;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[mode] built under RoomMenu with font '{(font != null ? font.name : "none")}' - "
                  + "move and restyle it in the scene from here");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        // Top left, matching how the rest of this menu is anchored.
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    static TMP_Text MakeText(Transform parent, string name, TMP_FontAsset font, float size,
                             TextAlignmentOptions alignment, Vector2 position, Vector2 dimensions)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.raycastTarget = false;

        if (font != null)
            text.font = font;

        Anchor((RectTransform)go.transform, new Vector2(0.5f, 0.5f), position + new Vector2(dimensions.x * 0.5f, 0f), dimensions);

        // Parent already positions it; children of the button fill the button.
        if (parent.GetComponent<Button>() != null)
        {
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        return text;
    }

    // Whatever the rest of the menu already uses, so this doesn't arrive in a different
    // typeface than everything around it.
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
