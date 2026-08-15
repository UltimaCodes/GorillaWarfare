using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opens the shipping scenes and looks for the damage you only notice at runtime.
//
// Deleting an asset that something still points at doesn't fail a compile and doesn't fail any
// of the other suites - it fails as a magenta wall or a missing script the first time somebody
// actually loads the level. Worth having a check that opens the thing.
public static class SceneCheck
{
    static readonly List<string> Failures = new List<string>();
    static readonly List<string> Notes = new List<string>();

    public static void Run()
    {
        Failures.Clear();
        Notes.Clear();

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

        if (scenes.Length == 0)
            Failures.Add("no scenes in build settings - a build would ship empty");

        foreach (EditorBuildSettingsScene entry in scenes)
        {
            if (!entry.enabled)
                continue;

            Scene scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
            Notes.Add($"--- {scene.name} ({scene.rootCount} roots)");

            CheckMissingScripts(scene);
            CheckMissingMaterials(scene);
        }

        // The game scene has to be the one the spawner waits for, and it has to have somewhere
        // to put people.
        CheckGameScene();
        CheckMenuScene();

        foreach (string note in Notes)
            Debug.Log($"[scene] {note}");

        foreach (string failure in Failures)
            Debug.LogError($"[scene] FAIL {failure}");

        Debug.Log(Failures.Count == 0 ? "[scene] ===== ALL PASS =====" : $"[scene] {Failures.Count} FAILURES");
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    // A component whose script asset is gone deserialises as null and silently does nothing.
    static void CheckMissingScripts(Scene scene)
    {
        int missing = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                Component[] components = t.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null)
                    {
                        Failures.Add($"{scene.name}: '{Path(t)}' has a missing script in slot {i}");
                        missing++;
                    }
                }
            }
        }

        Notes.Add($"{scene.name}: {missing} missing scripts");
    }

    // A renderer whose material was deleted renders magenta, which is easy to miss in a
    // screenshot and impossible to miss in a match.
    static void CheckMissingMaterials(Scene scene)
    {
        int broken = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;

                if (materials.Length == 0)
                {
                    Failures.Add($"{scene.name}: '{Path(renderer.transform)}' has no material at all");
                    broken++;
                    continue;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        Failures.Add($"{scene.name}: '{Path(renderer.transform)}' material slot {i} is empty - renders magenta");
                        broken++;
                    }
                }
            }
        }

        Notes.Add($"{scene.name}: {broken} broken material slots");
    }

    static void CheckGameScene()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);

        SpawnManager spawner = Object.FindFirstObjectByType<SpawnManager>();
        if (spawner == null)
        {
            Failures.Add("Game has no SpawnManager - RoomManager waits for it forever and nobody spawns");
            return;
        }

        Spawnpoint[] points = spawner.GetComponentsInChildren<Spawnpoint>(true);
        Notes.Add($"Game: {points.Length} spawnpoints");

        if (points.Length == 0)
            Failures.Add("no spawnpoints under SpawnManager - nobody can spawn");

        // Everyone landing on one pad is a spawn kill waiting to happen.
        if (points.Length < 4)
            Failures.Add($"only {points.Length} spawnpoints for an 8 player room");

        // The game scene must be build index 1 - RoomManager hard codes it.
        if (scene.buildIndex != 1)
            Failures.Add($"Game is build index {scene.buildIndex}, but the spawn path waits for index 1");

        CheckHud();
    }

    /// <summary>
    /// The HUD is a scene object rather than something built at runtime, which is the whole
    /// point of it - it can be moved, recoloured and re-fonted without touching C#.
    ///
    /// The cost of that is that it can also be half deleted. A missing reference doesn't throw;
    /// GameHud checks every slot before using it, so a dragged-away health bar just quietly
    /// stops appearing and looks like a bug in the health code. This walks every serialized
    /// reference and names the empty ones.
    /// </summary>
    static void CheckHud()
    {
        GameHud hud = Object.FindFirstObjectByType<GameHud>(FindObjectsInactive.Include);

        if (hud == null)
        {
            Failures.Add("Game has no GameHud - no health, ammo, crosshair, clock or kill feed. "
                         + "Run Tools/Gorilla Warfare/Build the in-game HUD");
            return;
        }

        if (hud.GetComponentInParent<Canvas>() == null)
            Failures.Add("GameHud is not under a Canvas, so none of it will draw");

        int empty = 0;
        SerializedProperty property = new SerializedObject(hud).GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyPath == "m_Script"
                || property.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            if (property.objectReferenceValue != null)
                continue;

            Failures.Add($"GameHud.{property.propertyPath} is empty - that part of the HUD is missing");
            empty++;
        }

        if (empty == 0)
            Notes.Add("Game: HUD is fully wired");
    }

    static void CheckMenuScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);

        if (Object.FindFirstObjectByType<Launcher>() == null)
            Failures.Add("Menu has no Launcher - nothing connects to Photon");

        if (Object.FindFirstObjectByType<MenuManager>() == null)
            Failures.Add("Menu has no MenuManager - no screen can ever open");

        if (Object.FindFirstObjectByType<RoomManager>() == null)
            Failures.Add("Menu has no RoomManager - match state and spawning never start");

        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            Failures.Add("Menu has no EventSystem - no button is clickable");
    }

    static string Path(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }

        return path;
    }
}
