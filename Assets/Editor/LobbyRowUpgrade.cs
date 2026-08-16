using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Makes the lobby list rows clickable, and hides the colour picker in a team mode.
//
// The row prefab was a label. Switching sides by clicking your own name needs it to be a button,
// which is one component and a wiring - not worth rebuilding the prefab over, and rebuilding it
// would throw away whatever styling has been done to it since.
public static class LobbyRowUpgrade
{
    const string RowPrefab = "Assets/Prefabs/PlayerListItem.prefab";

    [MenuItem("Tools/Gorilla Warfare/Upgrade the lobby rows")]
    public static void Run()
    {
        // LoadPrefabContents rather than LoadAssetAtPath, because you cannot add a component to
        // a prefab asset in place - AddComponent on one returns null and the next line throws.
        // This opens a real editable copy, which is what the prefab editing window does.
        GameObject prefab = PrefabUtility.LoadPrefabContents(RowPrefab);

        if (prefab == null)
        {
            Debug.LogError($"[lobby] no prefab at {RowPrefab}");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        bool changed = false;

        // A Button needs a Graphic to raycast against, and the row already has one: the label.
        //
        // Adding an Image alongside it does not work - a GameObject may only carry one Graphic,
        // and TextMeshProUGUI is one, so AddComponent quietly returns null and the next line
        // throws. Using the text itself is better anyway: TMP raycasts over its whole rect
        // rather than per glyph, so the entire row is clickable and there is no invisible
        // rectangle sitting on top of it.
        TMP_Text face = prefab.GetComponentInChildren<TMP_Text>(true);

        if (face == null)
        {
            Debug.LogError("[lobby] the row prefab has no label to click");
            PrefabUtility.UnloadPrefabContents(prefab);

            if (Application.isBatchMode)
                EditorApplication.Exit(1);
            return;
        }

        if (!face.raycastTarget)
        {
            face.raycastTarget = true;
            changed = true;
        }

        Button button = face.GetComponent<Button>();

        if (button == null)
        {
            button = face.gameObject.AddComponent<Button>();
            changed = true;
        }

        button.targetGraphic = face;

        // No tint. The label's colour is the team, and letting Unity wash it toward grey on
        // hover would fight the one thing the row is there to say.
        button.transition = Selectable.Transition.None;

        PlayerListItem row = prefab.GetComponentInChildren<PlayerListItem>(true);

        if (row != null)
        {
            SerializedObject so = new SerializedObject(row);
            SerializedProperty slot = so.FindProperty("button");

            if (slot != null && slot.objectReferenceValue != button)
            {
                slot.objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            // The label may not have been wired if the prefab predates the field.
            SerializedProperty label = so.FindProperty("text");

            if (label != null && label.objectReferenceValue == null)
            {
                label.objectReferenceValue = face;
                so.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
        }

        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(prefab, RowPrefab);
            Debug.Log("[lobby] rows are clickable now");
        }
        else
        {
            Debug.Log("[lobby] rows were already set up");
        }

        // The contents are a temporary scene under the hood. Leaving them loaded leaks it and
        // Unity complains on the next domain reload.
        PrefabUtility.UnloadPrefabContents(prefab);

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
