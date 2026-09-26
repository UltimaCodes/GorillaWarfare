using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds a "CRATES" button to the title menu, next to Settings - "add a button in the title menu
/// for accessing this crate stuff." Additive only: clones the existing SettingsButton (same
/// visual style, same TitleMenu > ButtonContainer parent, same VerticalLayoutGroup that already
/// auto-arranges its children) rather than hand-building a new one from scratch, then swaps its
/// OpenSettingsButton component for OpenCrateShopButton and retitles the label. Re-runnable -
/// does nothing if a button by this name already exists, rather than adding a second one on
/// every run.
/// </summary>
public static class TitleMenuCrateButton
{
    const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Gorilla Warfare/Add the title menu crate button")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform buttonContainer = FindByPath(scene, "Canvas/TitleMenu/ButtonContainer");

        if (buttonContainer == null)
        {
            Debug.LogError("[menu] could not find Canvas/TitleMenu/ButtonContainer");
            Finish(1);
            return;
        }

        if (buttonContainer.Find("CratesButton") != null)
        {
            Debug.Log("[menu] CratesButton already exists, nothing to do");
            Finish(0);
            return;
        }

        Transform settingsButton = buttonContainer.Find("SettingsButton");

        if (settingsButton == null)
        {
            Debug.LogError("[menu] could not find SettingsButton to clone");
            Finish(1);
            return;
        }

        GameObject clone = Object.Instantiate(settingsButton.gameObject, buttonContainer);
        clone.name = "CratesButton";

        // Sits right after Settings in sibling order, which is also its order in the
        // VerticalLayoutGroup - between "settings" and "quit" reads better than bracketing quit.
        clone.transform.SetSiblingIndex(settingsButton.GetSiblingIndex() + 1);

        Object.DestroyImmediate(clone.GetComponent<OpenSettingsButton>());
        clone.AddComponent<OpenCrateShopButton>();

        TMPro.TMP_Text label = clone.GetComponentInChildren<TMPro.TMP_Text>();

        if (label != null)
            label.text = "CRATES";

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[menu] added the CRATES button to the title menu, next to Settings");
        Finish(0);
    }

    static Transform FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != "Canvas")
                continue;

            Transform current = root.transform;

            for (int i = 1; i < parts.Length; i++)
            {
                current = current.Find(parts[i]);
                if (current == null)
                    return null;
            }

            return current;
        }

        return null;
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
