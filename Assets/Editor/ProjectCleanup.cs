using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Photon.Pun;

// Strips the 2024 leftovers out of the player prefab.
//
// Written as a script rather than done by hand in the inspector because prefab YAML is not
// something to edit with a text editor and hope, and because it has to be re-runnable - if a
// merge or an accidental apply ever puts the old weapons back, this takes them out again.
//
// Idempotent. Running it on an already clean prefab reports no changes and saves nothing.
public static class ProjectCleanup
{
    const string PrefabPath = "Assets/Resources/PhotonPrefabs/PlayerController.prefab";

    [MenuItem("Tools/Gorilla Warfare/Clean up the player prefab")]
    public static void Run()
    {
        List<string> changes = new List<string>();

        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[cleanup] no prefab at {PrefabPath}");
            EditorApplication.Exit(1);
            return;
        }

        StripHolder(prefab, changes);
        StripExtraViews(prefab, changes);
        ClearItemsArray(prefab, changes);
        StripDeadRenderers(prefab, changes);
        StripLegacyHealthbar(prefab, changes);

        if (changes.Count > 0)
        {
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            AssetDatabase.SaveAssets();
        }

        PrefabUtility.UnloadPrefabContents(prefab);

        foreach (string change in changes)
            Debug.Log($"[cleanup] {change}");

        Debug.Log(changes.Count == 0 ? "[cleanup] already clean" : $"[cleanup] {changes.Count} change(s), prefab saved");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static Transform FindChild(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }

    // The holder is filled at runtime by WeaponLoadout. Anything sitting in it in the asset is
    // the old M1911 and AK74, which is what every remote client was rendering on your hand.
    static void StripHolder(GameObject prefab, List<string> changes)
    {
        Transform holder = FindChild(prefab, "ItemHolder");
        if (holder == null)
        {
            Debug.LogWarning("[cleanup] no ItemHolder - skipping weapon strip");
            return;
        }

        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            GameObject child = holder.GetChild(i).gameObject;
            changes.Add($"removed leftover weapon '{child.name}' from ItemHolder");
            Object.DestroyImmediate(child);
        }
    }

    // Shots report through the player's view. The weapons carrying their own was the reason
    // runtime loadouts couldn't work in the first place.
    static void StripExtraViews(GameObject prefab, List<string> changes)
    {
        foreach (PhotonView view in prefab.GetComponentsInChildren<PhotonView>(true))
        {
            if (view.gameObject == prefab)
                continue;

            changes.Add($"removed a spare PhotonView from '{view.gameObject.name}'");
            Object.DestroyImmediate(view, true);
        }
    }

    // Serialized entries pointing at weapons that no longer exist. The array is rebuilt on spawn.
    static void ClearItemsArray(GameObject prefab, List<string> changes)
    {
        PlayerController controller = prefab.GetComponent<PlayerController>();
        if (controller == null)
            return;

        SerializedObject so = new SerializedObject(controller);
        SerializedProperty items = so.FindProperty("items");

        if (items == null || items.arraySize == 0)
            return;

        changes.Add($"cleared {items.arraySize} stale entries out of items[]");
        items.ClearArray();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // The screen space canvas carrying the original healthbar, and a label reading "Wont add
    // ammo or anything so heres a muaaz healthbar". Health is drawn by GameHud now, which
    // means it can react to being shot instead of quietly sliding down.
    static void StripLegacyHealthbar(GameObject prefab, List<string> changes)
    {
        PlayerController controller = prefab.GetComponent<PlayerController>();
        if (controller == null)
            return;

        SerializedObject so = new SerializedObject(controller);
        SerializedProperty ui = so.FindProperty("ui");
        SerializedProperty bar = so.FindProperty("healthbarImage");

        GameObject canvas = ui != null ? ui.objectReferenceValue as GameObject : null;

        if (canvas != null)
        {
            changes.Add($"removed the legacy healthbar canvas '{canvas.name}'");
            Object.DestroyImmediate(canvas);
        }

        bool cleared = false;

        if (ui != null && ui.objectReferenceValue != null)
        {
            ui.objectReferenceValue = null;
            cleared = true;
        }

        if (bar != null && bar.objectReferenceValue != null)
        {
            bar.objectReferenceValue = null;
            cleared = true;
        }

        if (cleared)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            changes.Add("cleared the healthbar references off PlayerController");
        }
    }

    // Imported with the original .3DS and disabled ever since. A disabled renderer still costs
    // a component to load and serialize on every player that spawns.
    static void StripDeadRenderers(GameObject prefab, List<string> changes)
    {
        foreach (MeshRenderer renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.enabled)
                continue;

            GameObject go = renderer.gameObject;
            changes.Add($"removed a disabled MeshRenderer from '{go.name}'");
            Object.DestroyImmediate(renderer, true);

            if (go.TryGetComponent(out MeshFilter filter))
                Object.DestroyImmediate(filter, true);
        }
    }
}
