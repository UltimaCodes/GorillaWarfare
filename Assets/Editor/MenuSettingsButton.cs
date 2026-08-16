using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Puts a SETTINGS button on the title screen.
//
// It clones one of the buttons already there rather than building a new one. Ryaan restyled
// every menu himself, and any button this script authored from scratch would arrive in the
// wrong font, the wrong colour and the wrong size no matter how carefully the numbers were
// copied - and would then drift the next time he changed the others. A copy of a real button is
// the only version that is guaranteed to match, and it keeps matching.
//
// Re-runnable: it replaces the button it made and leaves everything else alone.
public static class MenuSettingsButton
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string ButtonName = "SettingsButton";

    [MenuItem("Tools/Gorilla Warfare/Add the settings button to the menu")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform container = Find(scene, "ButtonContainer");

        if (container == null)
        {
            Debug.LogError("[settings] no ButtonContainer in the menu scene");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        Transform existing = container.Find(ButtonName);
        if (existing != null)
        {
            Debug.Log("[settings] replacing the previous settings button");
            Object.DestroyImmediate(existing.gameObject);
        }

        // Any sibling that is a button and isn't the one being made. Cloning the first is fine -
        // they are all the same style, which is the entire point.
        Button model = null;

        foreach (Button candidate in container.GetComponentsInChildren<Button>(true))
        {
            if (candidate.name != ButtonName)
            {
                model = candidate;
                break;
            }
        }

        if (model == null)
        {
            Debug.LogError("[settings] no button under ButtonContainer to copy the style from");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        GameObject made = Object.Instantiate(model.gameObject, container);
        made.name = ButtonName;
        made.SetActive(true);

        // Last in the container. If the buttons are laid out by a layout group this is enough;
        // if they are placed by hand, drop it below the others by the height of one button.
        made.transform.SetAsLastSibling();

        if (container.GetComponent<LayoutGroup>() == null)
        {
            RectTransform rect = (RectTransform)made.transform;
            RectTransform from = (RectTransform)model.transform;

            rect.anchoredPosition = from.anchoredPosition - new Vector2(0f, from.rect.height + 12f);
        }

        // Whatever the copied button used to do is not what this one does.
        Button button = made.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();

        foreach (TMP_Text label in made.GetComponentsInChildren<TMP_Text>(true))
            label.text = "SETTINGS";

        if (made.GetComponent<OpenSettingsButton>() == null)
            made.AddComponent<OpenSettingsButton>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[settings] added SETTINGS to the title screen, styled from '{model.name}'");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
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
