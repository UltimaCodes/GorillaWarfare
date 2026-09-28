using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The map system's one-time setup: the map list (`Resources/Maps.asset`), every map scene in
/// Build Settings with the menu first, and a map card in the lobby.
///
/// Each part only fills in what's missing, so this is safe to run again - an existing list keeps
/// its names, an existing map card keeps wherever it was moved to.
///
/// The map card is a copy of the lobby's own game mode card, so it arrives in the same style,
/// and it goes directly above that card - empty space in the lobby's right column - so nothing
/// already there moves.
/// </summary>
public static class MapSetup
{
    const string RegistryPath = "Assets/Resources/Maps.asset";
    const string MenuScenePath = "Assets/Scenes/Menu.unity";
    const float CardHeight = 175f;
    const float CardGap = 30f;

    const string BackdropName = "~MenuBackdrop";

    // Where the menu camera stands for each map's backdrop, picked from rendered candidates
    // (ZooBuilder.Photograph with GW_ZOO_VIEWS). The first map's is wherever the camera already is.
    // Pitch -2, the same slight downward look the arena's has.
    static readonly Dictionary<string, (Vector3 at, float yaw)> ViewSpots = new Dictionary<string, (Vector3, float)>
    {
        // The south avenue, up the path at the bandstand, trees either side.
        ["zoo"] = (new Vector3(-8f, 1.8f, -36f), 20f),
    };

    [MenuItem("Tools/Gorilla Warfare/Set up the maps")]
    public static void Run()
    {
        MapRegistry registry = EnsureRegistry();
        EnsureBuildSettings(registry);

        Scene menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        EnsureLobbyCard();
        EnsureBackdrops(menu);
        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// A copy of every map behind the menu, each with its own camera and gorilla markers, and a
    /// MenuBackdrop to show the lobby's pick. The first time, the copy that's already there becomes
    /// the first map's, with the camera and gorilla left exactly where they stand; each other map
    /// is copied in from its scene. Maps that already have a copy are left alone. Called by
    /// MenuBuilder's backdrop refresh too, so a refresh keeps every map. The caller saves.
    /// </summary>
    public static void EnsureBackdrops(Scene menu)
    {
        GameObject root = null;
        foreach (GameObject candidate in menu.GetRootGameObjects())
        {
            if (candidate.name == BackdropName)
                root = candidate;
        }

        MenuBackdropCamera menuCamera = null;
        foreach (GameObject candidate in menu.GetRootGameObjects())
        {
            if (menuCamera == null)
                menuCamera = candidate.GetComponentInChildren<MenuBackdropCamera>(true);
        }

        if (root == null || menuCamera == null)
        {
            Debug.LogError("[maps] no ~MenuBackdrop or menu camera in the menu - no per-map backdrops");
            return;
        }

        MenuGorilla gorilla = root.GetComponentInChildren<MenuGorilla>(true);
        MenuBackdrop backdrop = root.GetComponent<MenuBackdrop>();

        if (backdrop == null)
        {
            backdrop = root.AddComponent<MenuBackdrop>();

            // What's here is the first map's copy - into a group of its own, with the camera and
            // the gorilla recorded where they stand now.
            string first = MapRegistry.Default.key;
            GameObject group = Group(root, first);

            List<Transform> existing = new List<Transform>();
            foreach (Transform child in root.transform)
            {
                if (child.gameObject != group && (gorilla == null || child != gorilla.transform))
                    existing.Add(child);
            }

            foreach (Transform child in existing)
                child.SetParent(group.transform, true);

            AddView(backdrop, first, group, menuCamera.transform.position, menuCamera.transform.rotation,
                    gorilla != null ? gorilla.transform : null);
        }

        SerializedObject so = new SerializedObject(backdrop);
        so.FindProperty("menuCamera").objectReferenceValue = menuCamera;
        so.FindProperty("gorilla").objectReferenceValue = gorilla != null ? gorilla.transform : null;
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (MapRegistry.Map map in MapRegistry.All)
        {
            if (HasView(backdrop, map.key))
                continue;

            GameObject group = CopyMap(menu, root, map);
            if (group == null)
                continue;

            (Vector3 at, float yaw) spot = ViewSpots.TryGetValue(map.key, out var chosen)
                ? chosen : (new Vector3(0f, 1.8f, -20f), 0f);
            Quaternion look = Quaternion.Euler(-2f, spot.yaw, 0f);

            // The gorilla where the arena's stands relative to its camera - ahead and to the right,
            // on the ground, turned a little off facing the camera - found with only this copy live.
            Transform placedGorilla = PlaceGorilla(backdrop, group, spot.at, look);
            AddView(backdrop, map.key, group, spot.at, look, placedGorilla);
            Object.DestroyImmediate(placedGorilla.gameObject);

            group.SetActive(false);
            Debug.Log($"[maps] copied {map.displayName} in behind the menu, camera at {spot.at}");
        }
    }

    static GameObject Group(GameObject root, string name)
    {
        GameObject group = new GameObject(name);
        SceneManager.MoveGameObjectToScene(group, root.scene);
        group.transform.SetParent(root.transform, false);
        return group;
    }

    static bool HasView(MenuBackdrop backdrop, string key)
    {
        SerializedProperty views = new SerializedObject(backdrop).FindProperty("views");
        for (int i = 0; i < views.arraySize; i++)
        {
            if (views.GetArrayElementAtIndex(i).FindPropertyRelative("mapKey").stringValue == key)
                return true;
        }

        return false;
    }

    /// A map's geometry, sun and grass copied under its own group, the grass pointed at the copy's
    /// floor - the same copy MenuBuilder makes of the arena.
    static GameObject CopyMap(Scene menu, GameObject root, MapRegistry.Map map)
    {
        string path = $"Assets/Scenes/{map.sceneName}.unity";
        if (!System.IO.File.Exists(path))
        {
            Debug.LogWarning($"[maps] no {path} to copy behind the menu");
            return null;
        }

        GameObject group = Group(root, map.key);
        Scene source = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);

        foreach (GameObject candidate in source.GetRootGameObjects())
        {
            if (candidate.name != "Map" && candidate.name != "Grass" && candidate.GetComponent<Light>() == null)
                continue;

            GameObject copy = Object.Instantiate(candidate);
            copy.name = candidate.name;
            SceneManager.MoveGameObjectToScene(copy, menu);
            copy.transform.SetParent(group.transform, true);
        }

        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(menu);

        GrassField grass = group.GetComponentInChildren<GrassField>(true);
        Transform floor = null;
        foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
        {
            if (floor == null && t.name.StartsWith("Floor"))
                floor = t;
        }

        if (grass != null && floor != null)
        {
            Collider[] colliders = floor.GetComponentsInChildren<Collider>(true);
            SerializedObject so = new SerializedObject(grass);
            SerializedProperty list = so.FindProperty("ground");
            list.arraySize = colliders.Length;
            for (int i = 0; i < colliders.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        return group;
    }

    /// A marker where the gorilla should stand for a camera spot, worked out with only this map's
    /// copy active so the ground found is this map's.
    static Transform PlaceGorilla(MenuBackdrop backdrop, GameObject group, Vector3 cameraAt, Quaternion look)
    {
        List<GameObject> hidden = new List<GameObject>();
        foreach (Transform sibling in group.transform.parent)
        {
            if (sibling.gameObject != group && sibling.GetComponent<MenuGorilla>() == null && sibling.gameObject.activeSelf)
            {
                sibling.gameObject.SetActive(false);
                hidden.Add(sibling.gameObject);
            }
        }

        group.SetActive(true);
        Physics.SyncTransforms();

        Vector3 forward = look * Vector3.forward;
        Vector3 right = look * Vector3.right;
        Vector3 at = cameraAt + forward * 4.2f + right * 1.7f;

        if (Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out RaycastHit ground, 60f, 1, QueryTriggerInteraction.Ignore))
            at.y = ground.point.y;
        else
            at.y = 0f;

        Quaternion facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(cameraAt - at, Vector3.up), Vector3.up)
                            * Quaternion.Euler(0f, -28f, 0f);

        foreach (GameObject sibling in hidden)
            sibling.SetActive(true);

        GameObject marker = new GameObject("~gorillaPose");
        SceneManager.MoveGameObjectToScene(marker, group.scene);
        marker.transform.SetPositionAndRotation(at, facing);
        return marker.transform;
    }

    /// Adds a view with its two markers, as children of the map's group so they move with it.
    static void AddView(MenuBackdrop backdrop, string key, GameObject group, Vector3 cameraAt, Quaternion cameraLook,
                        Transform gorillaPose)
    {
        GameObject cameraSpot = new GameObject("CameraSpot");
        SceneManager.MoveGameObjectToScene(cameraSpot, group.scene);
        cameraSpot.transform.SetParent(group.transform, false);
        cameraSpot.transform.SetPositionAndRotation(cameraAt, cameraLook);

        GameObject gorillaSpot = new GameObject("GorillaSpot");
        SceneManager.MoveGameObjectToScene(gorillaSpot, group.scene);
        gorillaSpot.transform.SetParent(group.transform, false);
        if (gorillaPose != null)
            gorillaSpot.transform.SetPositionAndRotation(gorillaPose.position, gorillaPose.rotation);

        SerializedObject so = new SerializedObject(backdrop);
        SerializedProperty views = so.FindProperty("views");
        views.arraySize++;
        SerializedProperty view = views.GetArrayElementAtIndex(views.arraySize - 1);
        view.FindPropertyRelative("mapKey").stringValue = key;
        view.FindPropertyRelative("world").objectReferenceValue = group;
        view.FindPropertyRelative("cameraSpot").objectReferenceValue = cameraSpot.transform;
        view.FindPropertyRelative("gorillaSpot").objectReferenceValue = gorillaSpot.transform;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static MapRegistry EnsureRegistry()
    {
        MapRegistry registry = AssetDatabase.LoadAssetAtPath<MapRegistry>(RegistryPath);

        if (registry != null)
        {
            Debug.Log("[maps] map list already exists - left alone");
            return registry;
        }

        registry = ScriptableObject.CreateInstance<MapRegistry>();
        SerializedObject so = new SerializedObject(registry);
        SerializedProperty maps = so.FindProperty("maps");

        (string key, string name, string scene)[] rows =
        {
            ("jungle", "JUNGLE", "Game"),
            ("zoo", "THE ZOO", "Zoo"),
        };

        maps.arraySize = rows.Length;
        for (int i = 0; i < rows.Length; i++)
        {
            SerializedProperty row = maps.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("key").stringValue = rows[i].key;
            row.FindPropertyRelative("displayName").stringValue = rows[i].name;
            row.FindPropertyRelative("sceneName").stringValue = rows[i].scene;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[maps] created {RegistryPath} with {rows.Length} maps");
        return registry;
    }

    /// The menu first (RoomManager and the launcher treat index 0 as "not a map"), then every
    /// registered map whose scene exists.
    static void EnsureBuildSettings(MapRegistry registry)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        int added = 0;

        SerializedProperty maps = new SerializedObject(registry).FindProperty("maps");

        for (int i = 0; i < maps.arraySize; i++)
        {
            string sceneName = maps.GetArrayElementAtIndex(i).FindPropertyRelative("sceneName").stringValue;
            string path = $"Assets/Scenes/{sceneName}.unity";

            if (!System.IO.File.Exists(path) || scenes.Exists(s => s.path == path))
                continue;

            scenes.Add(new EditorBuildSettingsScene(path, true));
            added++;
        }

        int menu = scenes.FindIndex(s => s.path == MenuScenePath);
        if (menu > 0)
        {
            EditorBuildSettingsScene entry = scenes[menu];
            scenes.RemoveAt(menu);
            scenes.Insert(0, entry);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[maps] build settings: {added} map(s) added, "
                  + string.Join(", ", scenes.ConvertAll(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));
    }

    /// A copy of the game mode card, made into a map picker, placed above it.
    static void EnsureLobbyCard()
    {
        if (Object.FindFirstObjectByType<MapSelector>(FindObjectsInactive.Include) != null)
        {
            Debug.Log("[maps] the lobby already has a map picker - left alone");
            return;
        }

        ModeSelector mode = Object.FindFirstObjectByType<ModeSelector>(FindObjectsInactive.Include);
        RectTransform modeCard = mode != null ? mode.transform.parent as RectTransform : null;

        if (modeCard == null)
        {
            Debug.LogError("[maps] no game mode card in the lobby to copy - no map picker added");
            return;
        }

        GameObject cardObject = Object.Instantiate(modeCard.gameObject, modeCard.parent);
        cardObject.name = "MapCard";
        cardObject.transform.SetSiblingIndex(modeCard.GetSiblingIndex());
        RectTransform card = (RectTransform)cardObject.transform;

        // The copy's selector, and what it points at - already remapped onto the copy's own children.
        ModeSelector copied = cardObject.GetComponentInChildren<ModeSelector>(true);
        SerializedObject from = new SerializedObject(copied);
        Button button = from.FindProperty("button").objectReferenceValue as Button;
        TMP_Text label = from.FindProperty("label").objectReferenceValue as TMP_Text;
        TMP_Text readout = from.FindProperty("readout").objectReferenceValue as TMP_Text;
        TMP_Text description = from.FindProperty("description").objectReferenceValue as TMP_Text;

        GameObject selectorObject = copied.gameObject;
        selectorObject.name = "MapSelector";
        Object.DestroyImmediate(copied);

        // A map needs a name, not a paragraph - and the card is shorter so it fits above the mode.
        if (description != null)
            Object.DestroyImmediate(description.gameObject);

        MapSelector selector = selectorObject.AddComponent<MapSelector>();
        SerializedObject to = new SerializedObject(selector);
        to.FindProperty("button").objectReferenceValue = button;
        to.FindProperty("label").objectReferenceValue = label;
        to.FindProperty("readout").objectReferenceValue = readout;
        to.ApplyModifiedPropertiesWithoutUndo();

        foreach (TMP_Text text in cardObject.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.name == "Eyebrow")
                text.text = "MAP";
        }

        string first = MapRegistry.Default.displayName;
        if (label != null) label.text = first;
        if (readout != null) readout.text = first;

        card.sizeDelta = new Vector2(card.sizeDelta.x, CardHeight);
        RectTransform selectorRect = (RectTransform)selectorObject.transform;
        selectorRect.sizeDelta = new Vector2(selectorRect.sizeDelta.x, CardHeight);

        // Bottom edge a gap above the mode card's top edge, worked out in the parent's own space so
        // it holds wherever the mode card has been moved to by hand.
        float modeTop = modeCard.localPosition.y + modeCard.rect.yMax;
        float cardBottom = card.localPosition.y + card.rect.yMin;
        card.anchoredPosition += new Vector2(0f, modeTop + CardGap - cardBottom);

        Debug.Log($"[maps] added a map picker to the lobby, above the game mode card at {card.anchoredPosition}");
    }
}
