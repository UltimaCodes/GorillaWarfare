using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

        // Every map has to be loadable, and have somewhere to put people.
        CheckMaps();
        CheckMenuScene();
        CheckHud();
        CheckNothingIsOnTheDefaultFont();
        CheckEveryoneMeetsOnOneServer();

        // SettingsMenu and CrateOpeningScreen are RoomManager-instantiated Resources prefabs,
        // not scene objects - GameHud, ModeSelector and ColourPicker's own dropped-reference
        // hazard applies just as much to them, but FindFirstObjectByType would never find a
        // prefab asset, so these are checked directly rather than from either scene.
        CheckWiring(PrefabComponent<SettingsMenu>("SettingsMenu"), "SettingsMenu prefab");
        CheckWiring(PrefabComponent<CrateOpeningScreen>("CrateShop"), "CrateShop prefab");

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

    /// <summary>
    /// The grass (Assets/Grass + GrassField) fails silently: a missing compute shader or material
    /// is one warning in the log and no grass, and a GrassField with no ground colliders grows
    /// nothing at all. Re-run Tools/Gorilla Warfare/Set up the grass to fix any of these.
    /// </summary>
    static void CheckGrass(string map)
    {
        GrassField field = Object.FindFirstObjectByType<GrassField>();
        if (field == null)
        {
            Failures.Add($"{map} has no GrassField - no grass. Run Tools/Gorilla Warfare/Set up the grass");
            return;
        }

        GrassComputeScript compute = field.GetComponent<GrassComputeScript>();
        SO_GrassSettings settings = compute != null ? compute.currentPresets : null;

        if (settings == null)
            Failures.Add("the grass has no settings asset - GrassComputeScript draws nothing");
        else if (settings.shaderToUse == null || settings.materialToUse == null)
            Failures.Add("the grass settings are missing their compute shader or material");
        // Not Shader.isSupported - with -nographics there's no GPU to be supported by. A shader that
        // failed to compile or went missing shows up as Unity's error shader instead.
        else if (settings.materialToUse.shader == null || settings.materialToUse.shader.name == "Hidden/InternalErrorShader")
            Failures.Add("the grass material's shader is missing or failed to compile");

        SerializedProperty ground = new SerializedObject(field).FindProperty("ground");
        int wired = 0;
        for (int i = 0; ground != null && i < ground.arraySize; i++)
        {
            if (ground.GetArrayElementAtIndex(i).objectReferenceValue != null)
                wired++;
        }

        if (wired == 0)
            Failures.Add($"{map}'s GrassField has no ground colliders - it raycasts onto them to grow anything");
        else
            Notes.Add($"{map}: grass wired to {wired} ground collider(s), settings '{settings?.name}'");
    }

    /// <summary>
    /// Every map MapRegistry lists: in Build Settings, loadable, and fit to play on.
    ///
    /// This used to be one check of one scene that had to be build index 1, because RoomManager
    /// hard-coded that. Maps are found by name through the registry now, so what matters is that
    /// each one is there at all, and the menu is still index 0 - the one scene that isn't a map.
    /// </summary>
    static void CheckMaps()
    {
        EditorBuildSettingsScene[] built = EditorBuildSettings.scenes;

        if (built.Length == 0 || !built[0].path.EndsWith("/Menu.unity"))
            Failures.Add("the menu isn't build index 0 - a build would open straight into something else");

        if (AssetDatabase.LoadAssetAtPath<MapRegistry>("Assets/Resources/Maps.asset") == null)
            Failures.Add("no Resources/Maps.asset - only the fallback jungle is playable. Run Tools/Gorilla Warfare/Set up the maps");

        foreach (MapRegistry.Map map in MapRegistry.All)
        {
            string path = $"Assets/Scenes/{map.sceneName}.unity";

            if (!System.Array.Exists(built, s => s.enabled && s.path == path))
            {
                Failures.Add($"map '{map.key}' wants {path}, which isn't an enabled scene in Build Settings");
                continue;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Physics.SyncTransforms();

            CheckSpawns(map.sceneName);
            CheckGrass(map.sceneName);

            // The HUD is a prefab now (Resources/MatchHud) spawned into every map; a map carrying
            // its own as well would draw two of everything.
            if (Object.FindFirstObjectByType<GameHud>(FindObjectsInactive.Include) != null
                || Object.FindFirstObjectByType<Scoreboard>(FindObjectsInactive.Include) != null)
                Failures.Add($"{map.sceneName} has its own HUD - it gets the shared one when it loads, so this is a second");

            // Nothing on any screen can be clicked without one - the settings screen included.
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include) == null)
                Failures.Add($"{map.sceneName} has no EventSystem - nothing in a match can be clicked");

            Notes.Add($"{map.sceneName}: map '{map.key}' ({map.displayName}), build index {scene.buildIndex}");
        }
    }

    static void CheckSpawns(string map)
    {
        SpawnManager spawner = Object.FindFirstObjectByType<SpawnManager>();
        if (spawner == null)
        {
            Failures.Add($"{map} has no SpawnManager - RoomManager waits for it forever and nobody spawns");
            return;
        }

        Spawnpoint[] points = spawner.GetComponentsInChildren<Spawnpoint>(true);
        Notes.Add($"{map}: {points.Length} spawnpoints");

        // Half a twelve-seat room, so at worst two people share a pad. Below that and a full game
        // starts with several players inside each other.
        const int seats = 12;
        int wanted = Mathf.Max(4, seats / 2);

        if (points.Length < wanted)
            Failures.Add($"{map} has only {points.Length} spawnpoints for a {seats} player room - want {wanted}");

        // A pad over nothing drops you out of the map; a pad inside a wall leaves you stuck in it.
        // The pad's own editor marker has a collider, so it's left out of both tests - the first
        // version of this found every pad in the arena "inside something": itself.
        float highest = 0f;

        foreach (Spawnpoint point in points)
        {
            Vector3 at = point.transform.position;
            bool Own(Collider c) => c.transform.IsChildOf(point.transform);

            float drop = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 0.2f, Vector3.down, 3f,
                                                          Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            {
                if (!Own(hit.collider))
                    drop = Mathf.Min(drop, hit.distance - 0.2f);
            }

            if (drop == float.MaxValue)
                Failures.Add($"{map}: '{point.name}' at {at:F1} has no ground within 3m under it");
            else
                highest = Mathf.Max(highest, drop);

            foreach (Collider blocker in Physics.OverlapCapsule(at + Vector3.down * 0.4f, at + Vector3.up * 0.5f, 0.45f,
                                                                 Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            {
                if (Own(blocker))
                    continue;

                Failures.Add($"{map}: '{point.name}' at {at:F1} is inside '{blocker.name}' - there's no room to stand");
                break;
            }
        }

        Notes.Add($"{map}: every pad has ground under it, the highest {highest:F2}m up");
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
    static readonly System.Collections.Generic.HashSet<string> OptionalFields = new System.Collections.Generic.HashSet<string>
    {
        "tokenRewardBurst",
    };

    static void CheckHud()
    {
        // The shared prefab RoomManager spawns into every map - see MatchHudPrefab.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MatchHudPrefab.PrefabPath);
        GameHud hud = prefab != null ? prefab.GetComponentInChildren<GameHud>(true) : null;

        if (hud == null)
        {
            Failures.Add($"no GameHud in {MatchHudPrefab.PrefabPath} - no health, ammo, crosshair, clock or "
                         + "kill feed in any map");
            return;
        }

        if (prefab.GetComponentInChildren<Scoreboard>(true) == null)
            Failures.Add($"no Scoreboard in {MatchHudPrefab.PrefabPath} - Tab shows nothing");

        // Including inactive - a prefab asset's objects aren't active in any scene.
        if (hud.GetComponentInParent<Canvas>(true) == null)
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

            // tokenRewardBurst is deliberately unwired - GameHud.cs guards it with a null check
            // specifically because HudBuilder.cs leaves it as an optional particle flourish, not
            // a dropped reference. Flagging it here was a false positive on every single run;
            // re-pointing the check rather than deleting it, same convention working-notes.md
            // already uses for this class of thing.
            if (OptionalFields.Contains(property.propertyPath))
                continue;

            Failures.Add($"the HUD prefab's GameHud.{property.propertyPath} is empty - that part of the HUD is missing");
            empty++;
        }

        if (empty == 0)
            Notes.Add("HUD prefab is fully wired");
    }

    /// <summary>
    /// Nothing should be left on TextMeshPro's default font.
    ///
    /// LiberationSans is what TMP assigns when nobody chooses, so it's the reliable giveaway
    /// that a piece of UI was built and never styled. Four prefabs were still on it long after
    /// everything around them had been restyled, and the result was that parts of the game
    /// looked like they belonged to an older build - because they did.
    ///
    /// Prefabs rather than just scenes, since that is where all four of them were hiding.
    /// </summary>
    static void CheckNothingIsOnTheDefaultFont()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null)
                continue;

            foreach (TMPro.TMP_Text text in prefab.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (text.font != null && text.font.name.Contains("LiberationSans"))
                    Failures.Add($"{System.IO.Path.GetFileName(path)}/{text.name} is still on the "
                                 + "default font - give it one of the project's own");
            }
        }
    }

    /// <summary>
    /// Friends in different countries have to land on the same Photon cluster.
    ///
    /// Rooms exist per region. With no fixed region PUN connects everybody to their own nearest
    /// one, so a player in Italy and a player in Pakistan sit on different servers and each sees
    /// an empty room browser - which reads as the game being broken rather than as a setting.
    /// </summary>
    static void CheckEveryoneMeetsOnOneServer()
    {
        string region = PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion;

        if (string.IsNullOrEmpty(region))
        {
            Failures.Add("no FixedRegion - players in different countries will connect to "
                         + "different clusters and never see each other's rooms");
            return;
        }

        Notes.Add($"Net: everybody meets on '{region}'");

        if (string.IsNullOrEmpty(PhotonNetwork.PhotonServerSettings.AppSettings.AppVersion))
            Failures.Add("AppVersion is empty - mismatched builds will share rooms and desync");

        // Every RPC the game declares is in the shortcut list. One that isn't still works - PUN
        // sends its name as a string instead - which is exactly why four went in without anyone
        // noticing, and why nobody bumped AppVersion for them either. Adding one here is the
        // reminder: append it (never insert - the list's order is the wire format) and bump
        // AppVersion so a build without it can't share a room with one that has it.
        List<string> listed = PhotonNetwork.PhotonServerSettings.RpcList;
        foreach (System.Type type in typeof(PlayerController).Assembly.GetTypes())
        {
            if (type.Namespace != null && type.Namespace.StartsWith("Photon"))
                continue;

            foreach (System.Reflection.MethodInfo method in type.GetMethods(
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                         | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.DeclaredOnly))
            {
                if (method.IsDefined(typeof(PunRPC), false) && !listed.Contains(method.Name))
                    Failures.Add($"{type.Name}.{method.Name} is an RPC missing from PhotonServerSettings' RpcList "
                                 + "- append it and bump AppVersion");
            }
        }
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

        CheckWiring(Object.FindFirstObjectByType<ModeSelector>(FindObjectsInactive.Include), "ModeSelector");
        CheckWiring(Object.FindFirstObjectByType<MapSelector>(FindObjectsInactive.Include), "MapSelector");
        CheckWiring(Object.FindFirstObjectByType<ColourPicker>(FindObjectsInactive.Include), "ColourPicker");
        CheckWiring(Object.FindFirstObjectByType<Launcher>(FindObjectsInactive.Include), "Launcher");
        CheckMenuScreens();
        CheckMenuButtons();
    }

    /// <summary>
    /// Launcher and every button open screens by name ("title", "find room"...), and MenuManager
    /// only opens what's in its own list - a screen that's missing, renamed or left off the list
    /// is a button that does nothing, with one error in the log at the moment it's pressed.
    /// </summary>
    static void CheckMenuScreens()
    {
        MenuManager manager = Object.FindFirstObjectByType<MenuManager>(FindObjectsInactive.Include);
        if (manager == null)
            return;

        List<string> listed = new List<string>();
        SerializedProperty menus = new SerializedObject(manager).FindProperty("menus");
        for (int i = 0; menus != null && i < menus.arraySize; i++)
        {
            if (menus.GetArrayElementAtIndex(i).objectReferenceValue is Menu m)
                listed.Add(m.menuName);
        }

        foreach (string name in new[] { "loading", "title", "find room", "create room", "room", "error" })
        {
            int count = listed.FindAll(n => n == name).Count;
            if (count != 1)
                Failures.Add($"MenuManager lists '{name}' {count} times - it has to be exactly once, or Launcher opens nothing (or the wrong one)");
        }

        Notes.Add($"Menu: {listed.Count} screens listed ({string.Join(", ", listed)})");
    }

    /// <summary>
    /// A button with no click target doesn't error - it just does nothing when pressed. Every one
    /// in the menu needs either a persistent OnClick or a component that wires it at runtime.
    /// </summary>
    static void CheckMenuButtons()
    {
        int checkedCount = 0;

        foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Templates are stamped copies of, wired by whoever stamps them.
            if (button.name.Contains("Template"))
                continue;

            bool wiredAtRuntime = button.GetComponent<OpenSettingsButton>() != null
                                  || button.GetComponent<OpenCrateShopButton>() != null
                                  || button.GetComponentInParent<ModeSelector>(true) != null
                                  || button.GetComponentInParent<MapSelector>(true) != null;

            if (button.onClick.GetPersistentEventCount() == 0 && !wiredAtRuntime)
                Failures.Add($"menu button '{Path(button.transform)}' does nothing when clicked");

            checkedCount++;
        }

        Notes.Add($"Menu: {checkedCount} buttons, each wired to something");
    }

    static T PrefabComponent<T>(string resourceName) where T : Object
    {
        GameObject prefab = Resources.Load<GameObject>(resourceName);
        return prefab != null ? prefab.GetComponent<T>() : null;
    }

    /// <summary>
    /// The same "walk every serialized reference, name the empty ones" GameHud already gets in
    /// <see cref="CheckHud"/>, generalised for the other scene/prefab objects built the same
    /// hand-wired way (per each builder's own doc comment) and just as able to have a reference
    /// dragged loose by accident.
    /// </summary>
    static void CheckWiring(Object component, string label)
    {
        if (component == null)
        {
            Failures.Add($"{label} not found - nothing to check");
            return;
        }

        int empty = 0;
        SerializedProperty property = new SerializedObject(component).GetIterator();

        while (property.NextVisible(true))
        {
            if (property.propertyPath == "m_Script"
                || property.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            if (property.objectReferenceValue != null)
                continue;

            Failures.Add($"{label}.{property.propertyPath} is empty - dragged loose somewhere");
            empty++;
        }

        if (empty == 0)
            Notes.Add($"{label} is fully wired");
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
