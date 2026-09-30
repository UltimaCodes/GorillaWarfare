using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts INVENTORY on the title screen, under CRATES - a copy of the CRATES line itself, so it's
/// in the same style and wherever the menu's been moved to by hand, with its button pointed at the
/// inventory instead. Only adds it if there isn't one; nothing else in the menu is touched (the
/// menu is hand-edited - MenuBuilder is never run over it).
/// </summary>
public static class SkinMenuSetup
{
    const string MenuScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Gorilla Warfare/Add the inventory to the title screen")]
    public static void Run()
    {
        Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);

        if (Object.FindFirstObjectByType<OpenInventoryButton>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[skins] the title screen already has an inventory button - left alone");
            Finish(0);
            return;
        }

        OpenCrateShopButton crates = Object.FindFirstObjectByType<OpenCrateShopButton>(FindObjectsInactive.Include);
        if (crates == null)
        {
            Debug.LogError("[skins] no CRATES button on the title screen to copy");
            Finish(1);
            return;
        }

        GameObject copy = Object.Instantiate(crates.gameObject, crates.transform.parent);
        copy.name = "Inventory";
        copy.transform.SetSiblingIndex(crates.transform.GetSiblingIndex() + 1);

        Object.DestroyImmediate(copy.GetComponent<OpenCrateShopButton>());
        copy.AddComponent<OpenInventoryButton>();

        foreach (TMP_Text label in copy.GetComponentsInChildren<TMP_Text>(true))
        {
            if (label.text.Trim().ToUpperInvariant() == "CRATES")
                label.text = "INVENTORY";
        }

        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
        Debug.Log("[skins] added INVENTORY to the title screen, under CRATES");
        Finish(0);
    }

    static void Finish(int code)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }
}
