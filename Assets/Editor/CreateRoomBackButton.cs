using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Adds a "BACK" button to CreateRoomMenu - reported directly: "you have to create a room and
/// then leave it to go back to main menu." FindRoomMenu already has exactly this button, wired to
/// MenuManager.OpenMenu(TitleMenu) as a persistent listener the same way every other menu
/// transition in this scene is wired (found by reading the scene file directly - every one of
/// them calls MenuManager.OpenMenu with a Menu object argument, none of them go through a
/// component like OpenSettingsButton). Clones that button rather than hand-building a new one,
/// same reasoning TitleMenuCrateButton already used for CRATES.
/// </summary>
public static class CreateRoomBackButton
{
    const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Gorilla Warfare/Add the create-room back button")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform canvas = FindRoot(scene, "Canvas");

        if (canvas == null)
        {
            Debug.LogError("[menu] could not find Canvas");
            Finish(1);
            return;
        }

        Transform createRoomMenu = canvas.Find("CreateRoomMenu");
        Transform findRoomMenu = canvas.Find("FindRoomMenu");
        Transform titleMenu = canvas.Find("TitleMenu");

        if (createRoomMenu == null || findRoomMenu == null || titleMenu == null)
        {
            Debug.LogError("[menu] could not find CreateRoomMenu, FindRoomMenu or TitleMenu");
            Finish(1);
            return;
        }

        if (createRoomMenu.Find("Back") != null)
        {
            Debug.Log("[menu] CreateRoomMenu already has a Back button, nothing to do");
            Finish(0);
            return;
        }

        Transform sourceBack = findRoomMenu.Find("Back");

        if (sourceBack == null)
        {
            Debug.LogError("[menu] FindRoomMenu has no Back button to clone");
            Finish(1);
            return;
        }

        Menu titleMenuComponent = titleMenu.GetComponent<Menu>();
        MenuManager menuManager = canvas.GetComponent<MenuManager>();

        if (titleMenuComponent == null || menuManager == null)
        {
            Debug.LogError("[menu] TitleMenu has no Menu component, or Canvas has no MenuManager");
            Finish(1);
            return;
        }

        GameObject clone = Object.Instantiate(sourceBack.gameObject, createRoomMenu);
        clone.name = "Back";

        // Clears whatever persistent listener came across with the clone (FindRoomMenu's own
        // Back button, pointed at itself) before adding the real one below - AddPersistentListener
        // appends, it doesn't replace, and leaving the old one would open FindRoomMenu instead of
        // (or as well as) TitleMenu.
        Button button = clone.GetComponent<Button>();
        int existingCount = button.onClick.GetPersistentEventCount();

        for (int i = existingCount - 1; i >= 0; i--)
            UnityEventTools.RemovePersistentListener(button.onClick, i);

        UnityEventTools.AddObjectPersistentListener<Menu>(button.onClick,
            menuManager.OpenMenu, titleMenuComponent);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[menu] added a BACK button to CreateRoomMenu, wired to TitleMenu");
        Finish(0);
    }

    static Transform FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root.transform;
        }

        return null;
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
