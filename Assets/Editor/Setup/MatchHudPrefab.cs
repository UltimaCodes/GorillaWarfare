using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves the HUD and the tab scoreboard out of the Game scene into one shared prefab,
/// `Resources/MatchHud.prefab`, which RoomManager spawns into whichever map loads.
///
/// "Make it a shared player prefab instead of just putting the HUD on every single map" - the
/// HUD was hand-edited scene data inside the one map, and a second map would have needed a copy
/// of it with every future edit made twice. The objects move as they are, so every hand edit to
/// the layout survives.
///
/// Refuses if anything in the scene points into the HUD, or the HUD points out of itself: a
/// prefab can't hold a reference to a scene object, and the conversion would quietly empty it.
/// Refuses if the prefab already exists - this runs once.
/// </summary>
public static class MatchHudPrefab
{
    public const string PrefabPath = "Assets/Resources/MatchHud.prefab";
    const string ScenePath = "Assets/Scenes/Game.unity";
    static readonly string[] HudRoots = { "GameHud", "ScoreboardCanvas" };

    [MenuItem("Tools/Gorilla Warfare/Move the HUD into a shared prefab")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Fail($"{PrefabPath} already exists - the HUD has already been moved");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        List<GameObject> hud = new List<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (System.Array.IndexOf(HudRoots, root.name) >= 0)
                hud.Add(root);
        }

        if (hud.Count != HudRoots.Length)
        {
            Fail($"expected {string.Join(" and ", HudRoots)} at the root of the Game scene, found {hud.Count}");
            return;
        }

        string crossing = CrossingReferences(scene, hud);
        if (crossing.Length > 0)
        {
            Fail("references cross the HUD's edge, so a prefab would lose them:\n" + crossing);
            return;
        }

        int wiredBefore = WiredReferences(hud);

        GameObject holder = new GameObject("MatchHud");
        SceneManager.MoveGameObjectToScene(holder, scene);
        foreach (GameObject root in hud)
            root.transform.SetParent(holder.transform, false);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath, out bool success);
        if (!success || saved == null)
        {
            Fail("SaveAsPrefabAsset failed - the scene has not been saved, nothing changed");
            return;
        }

        int wiredAfter = WiredReferences(new List<GameObject> { saved });
        if (wiredAfter != wiredBefore)
        {
            AssetDatabase.DeleteAsset(PrefabPath);
            Fail($"the prefab kept {wiredAfter} of the HUD's {wiredBefore} references - removed it, "
                 + "the scene has not been saved");
            return;
        }

        Object.DestroyImmediate(holder);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[hud] moved {string.Join(" and ", HudRoots)} into {PrefabPath} with all {wiredAfter} "
                  + "references intact - RoomManager spawns it into every map now");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// Every serialized reference between a HUD object and a scene object outside the HUD, in
    /// either direction.
    static string CrossingReferences(Scene scene, List<GameObject> hud)
    {
        HashSet<Object> inside = new HashSet<Object>();
        foreach (GameObject root in hud)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                inside.Add(t.gameObject);
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c != null)
                        inside.Add(c);
                }
            }
        }

        StringBuilder found = new StringBuilder();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform)
                    continue;

                bool fromInside = inside.Contains(component);
                SerializedProperty property = new SerializedObject(component).GetIterator();

                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    // Scene objects only. TextMesh Pro keeps in-memory copies of its materials
                    // ("... (Instance)") that are neither in the HUD nor anywhere else in the
                    // scene - it rebuilds them at load, and a prefab has no need of them.
                    Object target = property.objectReferenceValue;
                    if (!IsSceneObject(target))
                        continue;

                    if (inside.Contains(target) != fromInside)
                        found.AppendLine($"  {component.GetType().Name} on '{component.name}'.{property.propertyPath} -> '{target.name}'");
                }
            }
        }

        return found.ToString();
    }

    /// How many non-empty object references the HUD's own components hold.
    static int WiredReferences(List<GameObject> roots)
    {
        int wired = 0;

        foreach (GameObject root in roots)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                SerializedProperty property = new SerializedObject(behaviour).GetIterator();
                while (property.NextVisible(true))
                {
                    // Saved assets and the hierarchy's own objects - what a prefab keeps. Not the
                    // in-memory material copies TextMesh Pro makes and remakes on its own.
                    Object target = property.propertyType == SerializedPropertyType.ObjectReference
                                    && property.propertyPath != "m_Script"
                                    ? property.objectReferenceValue : null;

                    if (target != null && (EditorUtility.IsPersistent(target) || target is GameObject || target is Component))
                        wired++;
                }
            }
        }

        return wired;
    }

    static bool IsSceneObject(Object target) =>
        target != null && !EditorUtility.IsPersistent(target) && (target is GameObject || target is Component);

    static void Fail(string why)
    {
        Debug.LogError("[hud] " + why);
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
