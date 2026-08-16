using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Turns the loading screen from a word into a screen.
//
// It was a single TMP label reading "loading" over whatever menu happened to be behind it, so
// there was no way to tell a slow connection from a hung one. Now it's a full backdrop and a
// bar, and no text at all - a bar that moves says everything the word did, and says it without
// anyone having to read.
//
// Re-runnable: replaces what it made, leaves the rest of the menu alone.
public static class LoadingScreenBuilder
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string RootName = "LoadingBar";

    static readonly Color Backdrop = new Color(0.02f, 0.02f, 0.03f, 1f);
    static readonly Color TrackColour = new Color(1f, 1f, 1f, 0.09f);
    static readonly Color Accent = new Color(1f, 0.42f, 0.06f);

    const float BarWidth = 900f;
    const float BarHeight = 16f;

    [MenuItem("Tools/Gorilla Warfare/Build the loading screen")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform loading = Find(scene, "LoadingMenu");

        if (loading == null)
        {
            Debug.LogError("[loading] no LoadingMenu in the menu scene");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        // The old label goes. Ryaan asked for the text out, and he's right - the bar carries the
        // whole message and a word sitting next to it is just something else to align.
        for (int i = loading.childCount - 1; i >= 0; i--)
        {
            Transform child = loading.GetChild(i);

            if (child.name == RootName || child.GetComponent<TMP_Text>() != null)
                Object.DestroyImmediate(child.gameObject);
        }

        GameObject root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(loading, false);
        Stretch((RectTransform)root.transform);

        // Opaque, not a dim. The menu behind it is mid-transition and showing half of it through
        // a loading screen is what made this look broken rather than busy.
        Image backdrop = root.AddComponent<Image>();
        backdrop.color = Backdrop;
        backdrop.raycastTarget = true;

        // Slightly below centre, where a progress bar belongs - dead centre reads as a divider
        // cutting the screen in half.
        GameObject track = Box(root.transform, "Track", new Vector2(0f, -60f),
                               new Vector2(BarWidth, BarHeight), TrackColour);

        GameObject fill = Box(track.transform, "Fill", Vector2.zero,
                              new Vector2(BarWidth * 0.25f, BarHeight), Accent);

        // Left pivoted and left anchored, because the script drives it by setting an x offset
        // and a width - a centre pivot would make both of those mean something else.
        RectTransform fillRect = (RectTransform)fill.transform;
        fillRect.anchorMin = fillRect.anchorMax = fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.anchoredPosition = Vector2.zero;

        // A hairline under the bar, the width of the screen. Gives the composition something to
        // sit on so the bar doesn't float in the middle of nothing.
        Box(root.transform, "Rule", new Vector2(0f, -120f), new Vector2(1600f, 1f),
            new Color(1f, 1f, 1f, 0.08f));

        LoadingScreen screen = loading.GetComponent<LoadingScreen>();

        if (screen == null)
            screen = loading.gameObject.AddComponent<LoadingScreen>();

        SerializedObject so = new SerializedObject(screen);
        so.FindProperty("track").objectReferenceValue = (RectTransform)track.transform;
        so.FindProperty("fill").objectReferenceValue = fillRect;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[loading] built - backdrop, bar, no text");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    static GameObject Box(Transform parent, string name, Vector2 position, Vector2 size, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;

        return go;
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
