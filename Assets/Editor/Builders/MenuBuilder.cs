using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the main menu: a live 3D backdrop (the real arena, copied from the Game scene, with a
/// gorilla holding a banana gun in front of it) and every menu screen - title, room browser,
/// create-a-lobby, the lobby itself, the error card and the loading screen - as real scene objects.
///
/// Reported directly: "we need a new main menu desperately, scrap the entire thing." References
/// were Portal 2, Black Ops II, Borderlands 2 and a room-list layout - left-aligned text menus over
/// a live scene, and a proper table for the lobby browser. The look is the in-game HUD's own
/// (HudBuilder): Anton for anything that shouts, Jersey10 for labels, the same near-black ink
/// outline and underlay, banana yellow as the one accent.
///
/// Like the HUD, the result is ordinary scene objects so it can be moved and restyled by hand -
/// which is also why this refuses to run over an existing menu. `Run` builds once; `Rebuild`
/// deliberately replaces everything, hand edits included. The backdrop is separate (`~MenuBackdrop`,
/// regenerated whole) so the arena copy can be refreshed after map changes without touching the UI.
/// </summary>
public static class MenuBuilder
{
    const string ScenePath = "Assets/Scenes/Menu.unity";
    const string GameScenePath = "Assets/Scenes/Game.unity";
    const string RootName = "MainMenu";
    const string BackdropName = "~MenuBackdrop";
    const string RoomRowPath = "Assets/Prefabs/UI/RoomListItem.prefab";
    const string PlayerRowPath = "Assets/Prefabs/UI/PlayerListItem.prefab";

    static readonly Vector2 Reference = new Vector2(1920f, 1080f);

    // ---- palette, taken from HudBuilder so the menu and the HUD are one visual language ----
    static readonly Color Ink = new Color(0.07f, 0.08f, 0.1f);          // HudBuilder.OutlineColour
    static readonly Color Banana = new Color(1f, 0.82f, 0.12f);         // the HUD's gold (#FFD11F)
    static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);         // the HUD's secondary text
    static readonly Color Danger = new Color(1f, 0.1f, 0.25f);          // the HUD's damage red
    static readonly Color PanelColour = new Color(0.035f, 0.04f, 0.05f, 0.86f);
    static readonly Color Rule = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Backing = new Color(0.03f, 0.035f, 0.045f, 1f);

    const float Margin = 140f;

    static TMP_FontAsset display;   // Anton
    static TMP_FontAsset body;      // Jersey10
    static Material displayInk;     // Anton with the HUD's outline + underlay
    static Material bodyInk;        // Jersey10 with the same
    static Material displayFlat;    // Anton, no outline - dark text on the yellow buttons

    [MenuItem("Tools/Gorilla Warfare/Build the main menu")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (FindRoot(scene, RootName) != null)
        {
            Debug.LogWarning($"[menu] '{RootName}' already exists - not overwriting what may have been edited by hand. "
                             + "Use Tools/Gorilla Warfare/Rebuild the main menu (replaces it) or delete it first.");
            Finish(0);
            return;
        }

        Build(scene);
        Finish(0);
    }

    [MenuItem("Tools/Gorilla Warfare/Rebuild the main menu (replaces hand edits)")]
    public static void Rebuild()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Build(scene);
        Finish(0);
    }

    [MenuItem("Tools/Gorilla Warfare/Refresh the main menu backdrop")]
    public static void RefreshBackdrop()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        BuildBackdrop(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Finish(0);
    }

    static void Build(Scene scene)
    {
        LoadStyle();

        // The old menu: one canvas carrying Launcher, MenuManager and PlayerNameManager with every
        // screen under it. Its Launcher's own number is the one setting worth keeping.
        byte maxPlayers = 12;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Launcher old = root.GetComponentInChildren<Launcher>(true);
            if (old != null)
                maxPlayers = (byte)new SerializedObject(old).FindProperty("maxPlayersPerRoom").intValue;
        }

        DestroyRoot(scene, "Canvas", r => r.GetComponent<MenuManager>() != null);
        DestroyRoot(scene, "Directional Light", r => r.GetComponent<Light>() != null);
        DestroyRoot(scene, RootName, r => true);

        BuildBackdrop(scene);

        GameObject roomRow = BuildRoomRow();
        GameObject playerRow = BuildPlayerRow();

        BuildUi(scene, maxPlayers, roomRow, playerRow);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[menu] built: backdrop, six screens, both row prefabs");
    }

    // =====================================================================================
    // backdrop
    // =====================================================================================

    /// <summary>
    /// The real arena behind the menu - copied, not referenced, from the Game scene (its Map, its
    /// sun and its grass) along with the Game scene's sky and ambient light, so the menu looks like
    /// the place you're about to fight in. A copy means map edits don't reach the menu until this
    /// runs again (Tools/Gorilla Warfare/Refresh the main menu backdrop).
    /// </summary>
    static void BuildBackdrop(Scene menu)
    {
        DestroyRoot(menu, BackdropName, r => true);

        GameObject backdrop = new GameObject(BackdropName);
        SceneManager.MoveGameObjectToScene(backdrop, menu);

        Scene game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);

        // Lighting belongs to whichever scene is active, so read it with the Game scene active
        // and write it with the Menu scene active.
        SceneManager.SetActiveScene(game);
        Material skybox = RenderSettings.skybox;
        UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
        float ambientIntensity = RenderSettings.ambientIntensity;
        Color ambientLight = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog;
        Color fogColour = RenderSettings.fogColor;
        FogMode fogMode = RenderSettings.fogMode;
        float fogDensity = RenderSettings.fogDensity;
        string sunName = RenderSettings.sun != null ? RenderSettings.sun.name : null;

        Light sun = null;

        foreach (GameObject root in game.GetRootGameObjects())
        {
            if (root.name != "Map" && root.name != "Grass" && root.GetComponent<Light>() == null)
                continue;

            GameObject copy = Object.Instantiate(root);
            copy.name = root.name;
            SceneManager.MoveGameObjectToScene(copy, menu);
            copy.transform.SetParent(backdrop.transform, true);

            if (copy.GetComponent<Light>() != null && (sun == null || root.name == sunName))
                sun = copy.GetComponent<Light>();
        }

        EditorSceneManager.CloseScene(game, true);
        SceneManager.SetActiveScene(menu);

        RenderSettings.skybox = skybox;
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.ambientLight = ambientLight;
        RenderSettings.fog = fog;
        RenderSettings.fogColor = fogColour;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.sun = sun;

        // The copied grass still points at the Game scene's floor - point it at the copy's.
        GrassField grass = backdrop.GetComponentInChildren<GrassField>(true);
        Transform floor = FindChild(backdrop.transform, t => t.name.StartsWith("Floor"));

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

        PlaceCamera(menu, floor);

        // Every other map in beside the arena, each with its own camera and gorilla spot, so the
        // lobby's backdrop can follow the lobby's map - see MenuBackdrop.
        MapSetup.EnsureBackdrops(menu);
    }

    /// <summary>
    /// The Main Camera becomes the backdrop camera - framed low, the arena behind, a gorilla on the
    /// right third of the frame where the menu isn't. Starting numbers; the camera and the
    /// "Menu Gorilla" are both ordinary objects to move afterwards.
    /// </summary>
    static void PlaceCamera(Scene menu, Transform floor)
    {
        Camera camera = null;
        foreach (GameObject root in menu.GetRootGameObjects())
        {
            if (camera == null)
                camera = root.GetComponent<Camera>();
        }

        if (camera == null)
        {
            GameObject host = new GameObject("Main Camera");
            SceneManager.MoveGameObjectToScene(host, menu);
            camera = host.AddComponent<Camera>();
            host.AddComponent<AudioListener>();
        }

        // Camera.main is what the grass culls against outside a match.
        camera.gameObject.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 55f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 400f;

        if (camera.GetComponent<MenuBackdropCamera>() == null)
            camera.gameObject.AddComponent<MenuBackdropCamera>();

        // The same toon outline the player camera draws, so the menu looks like the game.
        if (camera.GetComponent<ScreenOutline>() == null)
            camera.gameObject.AddComponent<ScreenOutline>();

        // Edit mode doesn't run the physics loop - the copied colliders aren't in the physics scene
        // until transforms are pushed across (same trap PlayerModelCheck notes).
        Physics.SyncTransforms();

        // Picked from rendered candidates (Tools/Gorilla Warfare/Photograph a map, GW_MAP_VIEWS):
        // at the west edge of the clearing looking east, across it to the idol on its temple - the
        // jungle's landmark behind the menu, the clearing open in front for the gorilla. Moved
        // 2026-09-30 when the jungle went onto terrain; the old spot is in the creek bed now.
        Vector3 spot = new Vector3(8f, 0f, 2f);
        const float yaw = 85f;

        float ground = floor != null ? floor.position.y : 0f;
        Collider floorCollider = floor != null ? floor.GetComponentInChildren<Collider>() : null;
        if (floorCollider != null && floorCollider.Raycast(new Ray(spot + Vector3.up * 50f, Vector3.down), out RaycastHit hit, 100f))
            ground = hit.point.y;

        camera.transform.position = new Vector3(spot.x, ground + 1.8f, spot.z);
        camera.transform.rotation = Quaternion.Euler(-2f, yaw, 0f);

        GameObject backdrop = FindRoot(menu, BackdropName);
        GameObject gorilla = new GameObject("Menu Gorilla");
        gorilla.transform.SetParent(backdrop.transform, false);

        Vector3 ahead = camera.transform.position + camera.transform.forward * 4.2f + camera.transform.right * 1.7f;
        ahead.y = ground;
        gorilla.transform.position = ahead;
        gorilla.transform.rotation = Quaternion.LookRotation(
            Vector3.ProjectOnPlane(camera.transform.position - ahead, Vector3.up), Vector3.up) * Quaternion.Euler(0f, -28f, 0f);

        gorilla.AddComponent<MenuGorilla>();
    }

    // =====================================================================================
    // ui
    // =====================================================================================

    static void BuildUi(Scene scene, byte maxPlayers, GameObject roomRow, GameObject playerRow)
    {
        GameObject rootObject = new GameObject(RootName, typeof(RectTransform), typeof(Canvas),
                                               typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(rootObject, scene);
        rootObject.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = rootObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = rootObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Reference;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        Transform root = rootObject.transform;

        MenuManager manager = rootObject.AddComponent<MenuManager>();
        Launcher launcher = rootObject.AddComponent<Launcher>();
        PlayerNameManager names = rootObject.AddComponent<PlayerNameManager>();

        // Built before the screens that link to them, so buttons can be wired as they're made.
        Menu loading = MakeScreen(root, "LoadingScreen", "loading");
        Menu title = MakeScreen(root, "TitleScreen", "title");
        Menu find = MakeScreen(root, "FindLobbyScreen", "find room");
        Menu create = MakeScreen(root, "CreateLobbyScreen", "create room");
        Menu room = MakeScreen(root, "LobbyScreen", "room");
        Menu error = MakeScreen(root, "ErrorScreen", "error");

        TMP_InputField username = BuildTitle(title.transform, manager, find, create, launcher, names);
        (Transform roomList, GameObject noRooms) = BuildFind(find.transform, manager, title, create);
        TMP_InputField roomName = BuildCreate(create.transform, manager, title, launcher);
        (TMP_Text roomTitle, Transform playerList, GameObject start) = BuildRoom(room.transform, launcher);
        TMP_Text errorText = BuildError(error.transform, manager, title);
        BuildLoading(loading.transform, rootObject);

        // The loading screen is the one showing on launch, while Photon connects.
        foreach (Menu m in new[] { title, find, create, room, error })
        {
            m.open = false;
            m.gameObject.SetActive(false);
        }
        loading.open = true;

        Wire(manager, "menus", new Object[] { loading, title, find, create, room, error });

        SerializedObject l = new SerializedObject(launcher);
        l.FindProperty("roomNameInputField").objectReferenceValue = roomName;
        l.FindProperty("errorText").objectReferenceValue = errorText;
        l.FindProperty("roomNameText").objectReferenceValue = roomTitle;
        l.FindProperty("roomListContent").objectReferenceValue = roomList;
        l.FindProperty("roomListItemPrefab").objectReferenceValue = roomRow;
        l.FindProperty("playerListItemPrefab").objectReferenceValue = playerRow;
        l.FindProperty("playerListContent").objectReferenceValue = playerList;
        l.FindProperty("startGameButton").objectReferenceValue = start;
        l.FindProperty("noRoomsMessage").objectReferenceValue = noRooms;
        l.FindProperty("maxPlayersPerRoom").intValue = maxPlayers;
        l.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject n = new SerializedObject(names);
        n.FindProperty("usernameInput").objectReferenceValue = username;
        n.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- title ----------------------------------------------------------------------------

    static TMP_InputField BuildTitle(Transform screen, MenuManager manager, Menu find, Menu create,
                                     Launcher launcher, PlayerNameManager names)
    {
        Logo(screen, new Vector2(Margin, -110f), 150f);

        TMP_Text tag = Label(screen, "Tagline", "FEATURING GORILLAS", body, bodyInk, 36f, Muted,
                             TextAlignmentOptions.TopLeft, TopLeft, new Vector2(Margin + 6f, -440f), new Vector2(900f, 50f));
        tag.characterSpacing = 8f;

        RectTransform nav = Rect(screen, "Nav", BottomLeft, BottomLeft, BottomLeft, new Vector2(Margin, 130f), new Vector2(700f, 440f));
        VerticalLayoutGroup layout = nav.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerLeft;
        layout.spacing = 2f;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Button play = NavItem(nav, "Play", "PLAY", 70f);
        UnityEventTools.AddObjectPersistentListener<Menu>(play.onClick, manager.OpenMenu, find);

        // No CREATE LOBBY here - the find screen PLAY opens has one, and two buttons doing the same
        // thing was one too many (Ryaan, 2026-09-28).

        // Both wired at runtime by their own components - the screens they open are Resources
        // prefabs RoomManager instantiates, so there's nothing in this scene to point at.
        NavItem(nav, "Crates", "CRATES", 70f).gameObject.AddComponent<OpenCrateShopButton>();
        NavItem(nav, "Settings", "SETTINGS", 70f).gameObject.AddComponent<OpenSettingsButton>();

        Button quit = NavItem(nav, "Quit", "QUIT", 70f);
        UnityEventTools.AddPersistentListener(quit.onClick, launcher.Quit);

        // ---- the player card, top right ----
        RectTransform card = Panel(screen, "PlayerCard", TopRight, TopRight, TopRight, new Vector2(-80f, -80f),
                                   new Vector2(580f, 196f), PanelColour);
        Bar(card, Banana, true);

        Label(card, "Eyebrow", "PLAYER", body, bodyInk, 28f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(34f, -20f), new Vector2(400f, 36f)).characterSpacing = 6f;

        TMP_InputField username = InputField(card, "UsernameInput", "Pick a username", 52f,
                                             TopLeft, new Vector2(34f, -58f), new Vector2(512f, 76f), false);
        UnityEventTools.AddVoidPersistentListener(username.onValueChanged, names.OnUserNameInputValueChanged);

        TMP_Text tokens = Label(card, "Tokens", "100 TOKENS", body, bodyInk, 32f, Banana,
                                TextAlignmentOptions.TopLeft, TopLeft, new Vector2(34f, -146f), new Vector2(400f, 40f));
        TokenReadout readout = tokens.gameObject.AddComponent<TokenReadout>();
        Wire(readout, "text", tokens);

        return username;
    }

    // ---- find a lobby ---------------------------------------------------------------------

    static (Transform content, GameObject empty) BuildFind(Transform screen, MenuManager manager, Menu title, Menu create)
    {
        Header(screen, "MULTIPLAYER", "FIND A LOBBY");

        RectTransform table = Panel(screen, "LobbyTable", TopLeft, TopLeft, TopLeft, new Vector2(Margin, -300f),
                                    new Vector2(1060f, 640f), PanelColour);

        ColumnHeading(table, "RoomNameHeading", "LOBBY", TextAlignmentOptions.TopLeft, new Vector2(28f, -22f));
        ColumnHeading(table, "ModeHeading", "MODE", TextAlignmentOptions.TopLeft, new Vector2(600f, -22f));
        ColumnHeading(table, "PlayersHeading", "PLAYERS", TextAlignmentOptions.TopRight, new Vector2(1032f - 200f, -22f));

        Panel(table, "Rule", TopLeft, TopLeft, TopLeft, new Vector2(0f, -66f), new Vector2(1060f, 2f), Rule);

        Transform content = ScrollList(table, "Rows", new Vector2(0f, -74f), new Vector2(1060f, 558f));

        RectTransform empty = Rect(table, "NoLobbies", Middle, Middle, Middle, new Vector2(0f, -30f), new Vector2(900f, 160f));
        Label(empty, "Title", "NO LOBBIES YET", display, displayInk, 56f, Muted, TextAlignmentOptions.Top,
              TopMiddle, new Vector2(0f, 0f), new Vector2(900f, 70f));
        Label(empty, "Hint", "Create one and it shows up here for everyone.", body, bodyInk, 32f, Muted,
              TextAlignmentOptions.Top, TopMiddle, new Vector2(0f, -80f), new Vector2(900f, 40f));

        RectTransform side = Rect(screen, "Side", TopLeft, TopLeft, TopLeft, new Vector2(Margin + 1060f + 60f, -300f), new Vector2(520f, 300f));
        Label(side, "Eyebrow", "HOST YOUR OWN", body, bodyInk, 28f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, Vector2.zero, new Vector2(520f, 36f)).characterSpacing = 6f;
        Button make = PrimaryButton(side, "CreateLobby", "CREATE LOBBY", TopLeft, new Vector2(0f, -50f), new Vector2(520f, 92f));
        UnityEventTools.AddObjectPersistentListener<Menu>(make.onClick, manager.OpenMenu, create);

        BackButton(screen, "BACK", manager, title);

        return (content, empty.gameObject);
    }

    // ---- create a lobby -------------------------------------------------------------------

    static TMP_InputField BuildCreate(Transform screen, MenuManager manager, Menu title, Launcher launcher)
    {
        Header(screen, "MULTIPLAYER", "CREATE A LOBBY");

        RectTransform card = Panel(screen, "NameCard", TopLeft, TopLeft, TopLeft, new Vector2(Margin, -300f),
                                   new Vector2(920f, 280f), PanelColour);
        Bar(card, Banana, true);

        Label(card, "Eyebrow", "LOBBY NAME", body, bodyInk, 28f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(40f, -26f), new Vector2(600f, 36f)).characterSpacing = 6f;

        TMP_InputField field = InputField(card, "LobbyNameInput", "Name your lobby", 58f,
                                          TopLeft, new Vector2(40f, -70f), new Vector2(840f, 100f), true);

        Label(card, "Hint", "Up to 24 characters. Everyone browsing lobbies sees it.", body, bodyInk, 28f, Muted,
              TextAlignmentOptions.TopLeft, TopLeft, new Vector2(40f, -196f), new Vector2(840f, 40f));

        Button go = PrimaryButton(screen, "Create", "CREATE", TopLeft, new Vector2(Margin, -620f), new Vector2(420f, 92f));
        UnityEventTools.AddPersistentListener(go.onClick, launcher.CreateRoom);

        BackButton(screen, "BACK", manager, title);

        return field;
    }

    // ---- the lobby ------------------------------------------------------------------------

    static (TMP_Text title, Transform players, GameObject start) BuildRoom(Transform screen, Launcher launcher)
    {
        Label(screen, "Eyebrow", "LOBBY", body, bodyInk, 32f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(Margin + 4f, -108f), new Vector2(800f, 40f)).characterSpacing = 8f;

        // Launcher writes the room's name here on joining.
        TMP_Text roomTitle = Label(screen, "RoomName", "LOBBY NAME", display, displayInk, 110f, Color.white,
                                   TextAlignmentOptions.TopLeft, TopLeft, new Vector2(Margin, -140f), new Vector2(1600f, 170f));
        roomTitle.textWrappingMode = TextWrappingModes.NoWrap;
        roomTitle.overflowMode = TextOverflowModes.Ellipsis;

        // ---- players ----
        RectTransform list = Panel(screen, "Players", TopLeft, TopLeft, TopLeft, new Vector2(Margin, -300f),
                                   new Vector2(760f, 580f), PanelColour);
        ColumnHeading(list, "Heading", "PLAYERS", TextAlignmentOptions.TopLeft, new Vector2(28f, -22f));
        Panel(list, "Rule", TopLeft, TopLeft, TopLeft, new Vector2(0f, -66f), new Vector2(760f, 2f), Rule);

        RectTransform players = Rect(list, "PlayerList", TopLeft, TopLeft, TopLeft, new Vector2(20f, -80f), new Vector2(720f, 430f));
        VerticalLayoutGroup rows = players.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 6f;
        rows.childControlWidth = true;
        rows.childControlHeight = false;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;

        Label(list, "Hint", "In a team mode, click your name to switch sides.", body, bodyInk, 26f, Muted,
              TextAlignmentOptions.BottomLeft, BottomLeft, new Vector2(28f, 20f), new Vector2(704f, 36f));

        // ---- game mode ----
        float right = Margin + 760f + 60f;

        RectTransform modeCard = Panel(screen, "ModeCard", TopLeft, TopLeft, TopLeft, new Vector2(right, -300f),
                                       new Vector2(800f, 280f), PanelColour);
        Label(modeCard, "Eyebrow", "GAME MODE", body, bodyInk, 28f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(34f, -22f), new Vector2(600f, 36f)).characterSpacing = 6f;

        RectTransform selectorRoot = Rect(modeCard, "ModeSelector", TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(800f, 280f));
        ModeSelector selector = selectorRoot.gameObject.AddComponent<ModeSelector>();

        Button cycle = TextButton(selectorRoot, "CycleButton", "DEATHMATCH", 66f, TopLeft, new Vector2(34f, -62f), new Vector2(740f, 84f));
        TMP_Text cycleLabel = cycle.transform.Find("Label").GetComponent<TMP_Text>();
        // Up in the card's heading row rather than beside the name - "TEAM DEATHMATCH" at this size
        // runs most of the way across. A child of the button, so it hides with it for non-hosts.
        Label(cycle.transform, "Hint", "CLICK TO CHANGE", body, bodyInk, 24f, Muted, TextAlignmentOptions.TopRight,
              TopRight, new Vector2(-4f, 40f), new Vector2(320f, 36f)).characterSpacing = 4f;

        TMP_Text readout = Label(selectorRoot, "Readout", "DEATHMATCH", display, displayInk, 66f, Color.white,
                                 TextAlignmentOptions.MidlineLeft, TopLeft, new Vector2(34f, -62f), new Vector2(740f, 84f));

        TMP_Text description = Label(selectorRoot, "Description", "a random banana every life", body, bodyInk, 32f, Muted,
                                     TextAlignmentOptions.TopLeft, TopLeft, new Vector2(34f, -160f), new Vector2(740f, 100f));
        description.textWrappingMode = TextWrappingModes.Normal;

        SerializedObject s = new SerializedObject(selector);
        s.FindProperty("button").objectReferenceValue = cycle;
        s.FindProperty("label").objectReferenceValue = cycleLabel;
        s.FindProperty("description").objectReferenceValue = description;
        s.FindProperty("readout").objectReferenceValue = readout;
        s.ApplyModifiedPropertiesWithoutUndo();

        // ---- banana colour ----
        RectTransform colourCard = Panel(screen, "ColourCard", TopLeft, TopLeft, TopLeft, new Vector2(right, -610f),
                                         new Vector2(800f, 280f), PanelColour);
        Label(colourCard, "Eyebrow", "YOUR BANANA", body, bodyInk, 28f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(34f, -22f), new Vector2(600f, 36f)).characterSpacing = 6f;

        RectTransform pickerRoot = Rect(colourCard, "ColourPicker", TopLeft, TopLeft, TopLeft, Vector2.zero, new Vector2(800f, 280f));
        ColourPicker picker = pickerRoot.gameObject.AddComponent<ColourPicker>();

        // ColourPicker writes the chosen colour's name here (or why there's no choice in a team mode).
        TMP_Text caption = Label(pickerRoot, "Caption", "BANANA", display, displayInk, 44f, Banana,
                                 TextAlignmentOptions.TopLeft, TopLeft, new Vector2(34f, -62f), new Vector2(740f, 56f));

        RectTransform grid = Rect(pickerRoot, "Row", TopLeft, TopLeft, TopLeft, new Vector2(34f, -126f), new Vector2(740f, 140f));
        GridLayoutGroup cells = grid.gameObject.AddComponent<GridLayoutGroup>();
        cells.cellSize = new Vector2(56f, 56f);
        cells.spacing = new Vector2(14f, 14f);
        cells.startCorner = GridLayoutGroup.Corner.UpperLeft;
        cells.childAlignment = TextAnchor.UpperLeft;

        GameObject swatch = new GameObject("SwatchTemplate", typeof(RectTransform), typeof(Image), typeof(Button));
        swatch.transform.SetParent(grid, false);
        swatch.layer = grid.gameObject.layer;
        Image face = swatch.GetComponent<Image>();
        face.color = Color.white;
        Button swatchButton = swatch.GetComponent<Button>();
        swatchButton.targetGraphic = face;
        swatch.SetActive(false);

        SerializedObject p = new SerializedObject(picker);
        p.FindProperty("row").objectReferenceValue = grid;
        p.FindProperty("swatchTemplate").objectReferenceValue = swatchButton;
        p.FindProperty("caption").objectReferenceValue = caption;
        p.ApplyModifiedPropertiesWithoutUndo();

        // ---- start / leave ----
        Button start = PrimaryButton(screen, "StartGame", "START GAME", BottomRight, new Vector2(-Margin, 80f), new Vector2(460f, 96f));
        UnityEventTools.AddPersistentListener(start.onClick, launcher.StartGame);

        Button leave = TextButton(screen, "Leave", "LEAVE LOBBY", 52f, BottomLeft, new Vector2(Margin, 90f), new Vector2(520f, 70f));
        UnityEventTools.AddPersistentListener(leave.onClick, launcher.LeaveRoom);

        return (roomTitle, players, start.gameObject);
    }

    // ---- error ----------------------------------------------------------------------------

    static TMP_Text BuildError(Transform screen, MenuManager manager, Menu title)
    {
        // Full-screen and raycast-blocking, so nothing behind the card can be clicked through it.
        RectTransform dim = Panel(screen, "Dim", Vector2.zero, Vector2.one, Middle, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.62f));
        dim.GetComponent<Image>().raycastTarget = true;

        RectTransform card = Panel(screen, "Card", Middle, Middle, Middle, Vector2.zero, new Vector2(1040f, 460f),
                                   new Color(0.035f, 0.04f, 0.05f, 0.94f));
        Bar(card, Danger, true);

        Label(card, "Eyebrow", "SOMETHING WENT WRONG", body, bodyInk, 30f, Danger, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(56f, -44f), new Vector2(900f, 40f)).characterSpacing = 6f;

        // Launcher writes the actual message here.
        TMP_Text message = Label(card, "Message", "Error Text", display, displayInk, 54f, Color.white,
                                 TextAlignmentOptions.TopLeft, TopLeft, new Vector2(56f, -100f), new Vector2(928f, 220f));
        message.textWrappingMode = TextWrappingModes.Normal;
        message.overflowMode = TextOverflowModes.Ellipsis;

        Button ok = PrimaryButton(card, "Ok", "OK", BottomLeft, new Vector2(56f, 48f), new Vector2(300f, 88f));
        UnityEventTools.AddObjectPersistentListener<Menu>(ok.onClick, manager.OpenMenu, title);

        return message;
    }

    // ---- loading --------------------------------------------------------------------------

    static void BuildLoading(Transform screen, GameObject menuRoot)
    {
        Panel(screen, "Backing", Vector2.zero, Vector2.one, Middle, Vector2.zero, Vector2.zero, Backing)
            .GetComponent<Image>().raycastTarget = true;

        Logo(screen, new Vector2(80f, -64f), 64f);

        // The spinning gorilla - "the gorilla spinning meme" - kept from the old loading screen,
        // bigger, and dead centre.
        RectTransform spinner = Rect(screen, "Spinner", Middle, Middle, Middle, new Vector2(0f, 50f), new Vector2(640f, 640f));
        spinner.gameObject.AddComponent<RawImage>().raycastTarget = false;
        spinner.gameObject.AddComponent<LoadingScreenSpinner>();

        Label(screen, "Loading", "LOADING", display, displayInk, 58f, Color.white, TextAlignmentOptions.Top,
              Middle, new Vector2(0f, -300f), new Vector2(800f, 70f)).characterSpacing = 10f;

        RectTransform track = Panel(screen, "Track", Middle, Middle, Middle, new Vector2(0f, -392f), new Vector2(720f, 10f),
                                    new Color(1f, 1f, 1f, 0.1f));
        RectTransform fill = Panel(track, "Fill", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                   Vector2.zero, new Vector2(180f, 10f), Banana);

        LoadingScreen bar = screen.gameObject.AddComponent<LoadingScreen>();
        SerializedObject so = new SerializedObject(bar);
        so.FindProperty("track").objectReferenceValue = track;
        so.FindProperty("fill").objectReferenceValue = fill;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // =====================================================================================
    // the two row prefabs Launcher instantiates
    // =====================================================================================

    static GameObject BuildRoomRow()
    {
        LoadStyle();

        GameObject row = RowBase("RoomListItem", 66f);

        TMP_Text name = Label(row.transform, "Name", "LOBBY", display, displayInk, 38f, Color.white,
                              TextAlignmentOptions.MidlineLeft, MiddleLeft, new Vector2(28f, 0f), new Vector2(550f, 60f));
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.richText = false;

        TMP_Text mode = Label(row.transform, "Mode", "DEATHMATCH", body, bodyInk, 32f, new Color(1f, 1f, 1f, 0.8f),
                              TextAlignmentOptions.MidlineLeft, MiddleLeft, new Vector2(600f, 0f), new Vector2(300f, 60f));

        TMP_Text players = Label(row.transform, "Players", "1/12", body, bodyInk, 32f, Color.white,
                                 TextAlignmentOptions.MidlineRight, MiddleRight, new Vector2(-28f, 0f), new Vector2(160f, 60f));

        RoomListItem item = row.AddComponent<RoomListItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("text").objectReferenceValue = name;
        so.FindProperty("modeText").objectReferenceValue = mode;
        so.FindProperty("playersText").objectReferenceValue = players;
        so.ApplyModifiedPropertiesWithoutUndo();

        UnityEventTools.AddPersistentListener(row.GetComponent<Button>().onClick, item.OnClick);

        return SaveRow(row, RoomRowPath);
    }

    static GameObject BuildPlayerRow()
    {
        LoadStyle();

        GameObject row = RowBase("PlayerListItem", 58f);

        // PlayerListItem colours this text in the player's own colour; the ink outline keeps any
        // of them readable against the dark row.
        TMP_Text name = Label(row.transform, "Name", "PLAYER", display, displayInk, 34f, Color.white,
                              TextAlignmentOptions.MidlineLeft, MiddleLeft, new Vector2(24f, 0f), new Vector2(660f, 58f));
        name.overflowMode = TextOverflowModes.Ellipsis;

        PlayerListItem item = row.AddComponent<PlayerListItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("text").objectReferenceValue = name;
        so.FindProperty("button").objectReferenceValue = row.GetComponent<Button>();
        so.ApplyModifiedPropertiesWithoutUndo();

        return SaveRow(row, PlayerRowPath);
    }

    /// A row: a faint strip that lights banana when hovered, sized by the list's layout group.
    static GameObject RowBase(string name, float height)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        row.layer = LayerMask.NameToLayer("UI");

        Image strip = row.GetComponent<Image>();
        strip.color = Color.white;

        Button button = row.GetComponent<Button>();
        button.targetGraphic = strip;
        ColorBlock colours = button.colors;
        colours.normalColor = new Color(1f, 1f, 1f, 0.04f);
        colours.highlightedColor = new Color(Banana.r, Banana.g, Banana.b, 0.22f);
        colours.pressedColor = new Color(Banana.r, Banana.g, Banana.b, 0.36f);
        colours.selectedColor = colours.normalColor;
        colours.disabledColor = new Color(1f, 1f, 1f, 0.04f);
        colours.fadeDuration = 0.08f;
        button.colors = colours;

        LayoutElement element = row.GetComponent<LayoutElement>();
        element.preferredHeight = height;
        element.minHeight = height;
        element.flexibleWidth = 1f;

        ((RectTransform)row.transform).sizeDelta = new Vector2(1000f, height);
        return row;
    }

    static GameObject SaveRow(GameObject row, string path)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(row, path);
        Object.DestroyImmediate(row);
        return saved;
    }

    // =====================================================================================
    // pieces
    // =====================================================================================

    static readonly Vector2 TopLeft = new Vector2(0f, 1f);
    static readonly Vector2 TopMiddle = new Vector2(0.5f, 1f);
    static readonly Vector2 TopRight = new Vector2(1f, 1f);
    static readonly Vector2 MiddleLeft = new Vector2(0f, 0.5f);
    static readonly Vector2 Middle = new Vector2(0.5f, 0.5f);
    static readonly Vector2 MiddleRight = new Vector2(1f, 0.5f);
    static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
    static readonly Vector2 BottomRight = new Vector2(1f, 0f);

    static Menu MakeScreen(Transform root, string name, string menuName)
    {
        RectTransform rect = Rect(root, name, Vector2.zero, Vector2.one, Middle, Vector2.zero, Vector2.zero);
        Menu menu = rect.gameObject.AddComponent<Menu>();
        menu.menuName = menuName;
        return menu;
    }

    /// "GORILLA" over "WARFARE", the second in banana - the game's name as its own logo.
    static void Logo(Transform parent, Vector2 topLeft, float size)
    {
        RectTransform logo = Rect(parent, "Logo", TopLeft, TopLeft, TopLeft, topLeft, new Vector2(size * 7f, size * 1.9f));
        Label(logo, "Gorilla", "GORILLA", display, displayInk, size, Color.white, TextAlignmentOptions.TopLeft,
              TopLeft, Vector2.zero, new Vector2(size * 7f, size * 1.05f));
        Label(logo, "Warfare", "WARFARE", display, displayInk, size, Banana, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(0f, -size * 0.95f), new Vector2(size * 7f, size * 1.05f));
    }

    /// A small spaced-out label over a big Anton title - every sub-screen's heading.
    static void Header(Transform screen, string eyebrow, string title)
    {
        Label(screen, "Eyebrow", eyebrow, body, bodyInk, 32f, Muted, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(Margin + 4f, -108f), new Vector2(800f, 40f)).characterSpacing = 8f;
        Label(screen, "Title", title, display, displayInk, 110f, Color.white, TextAlignmentOptions.TopLeft,
              TopLeft, new Vector2(Margin, -140f), new Vector2(1600f, 130f));
    }

    static void ColumnHeading(Transform parent, string name, string text, TextAlignmentOptions alignment, Vector2 position)
    {
        Label(parent, name, text, body, bodyInk, 28f, Muted, alignment, TopLeft, position, new Vector2(200f, 36f))
            .characterSpacing = 6f;
    }

    static void BackButton(Transform screen, string text, MenuManager manager, Menu to)
    {
        Button back = TextButton(screen, "Back", text, 52f, BottomLeft, new Vector2(Margin, 90f), new Vector2(360f, 70f));
        UnityEventTools.AddObjectPersistentListener<Menu>(back.onClick, manager.OpenMenu, to);
    }

    /// One line of the title screen's menu - see TextButton.
    static Button NavItem(Transform nav, string name, string text, float size)
    {
        Button button = TextButton(nav, name, text, size, TopLeft, Vector2.zero, new Vector2(700f, size * 1.2f));
        LayoutElement element = button.gameObject.AddComponent<LayoutElement>();
        element.preferredHeight = size * 1.2f;
        return button;
    }

    /// <summary>
    /// A button that's just its words - no box - lighting banana with an accent bar beside it on
    /// hover or focus (MenuButton). The invisible backing image is the hit area.
    /// </summary>
    static Button TextButton(Transform parent, string name, string text, float size, Vector2 anchor,
                             Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);

        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);

        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        RectTransform accent = Panel(rect, "Accent", MiddleLeft, MiddleLeft, MiddleLeft, new Vector2(-26f, 0f),
                                     new Vector2(8f, size * 0.72f), new Color(Banana.r, Banana.g, Banana.b, 0f));
        accent.GetComponent<Image>().raycastTarget = false;

        TMP_Text label = Label(rect, "Label", text, display, displayInk, size, Color.white, TextAlignmentOptions.MidlineLeft,
                               MiddleLeft, Vector2.zero, new Vector2(dimensions.x, dimensions.y));

        MenuButton look = rect.gameObject.AddComponent<MenuButton>();
        SerializedObject so = new SerializedObject(look);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("accent").objectReferenceValue = accent.GetComponent<Image>();
        so.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    /// The one filled button per screen - banana, dark Anton - for the thing the screen is for.
    static Button PrimaryButton(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);

        Image face = rect.gameObject.AddComponent<Image>();
        face.color = Banana;

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = face;
        ColorBlock colours = button.colors;
        colours.normalColor = new Color(0.9f, 0.9f, 0.9f);
        colours.highlightedColor = Color.white;
        colours.selectedColor = new Color(0.9f, 0.9f, 0.9f);
        colours.pressedColor = new Color(0.72f, 0.72f, 0.72f);
        colours.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        colours.colorMultiplier = 1.1f;
        colours.fadeDuration = 0.08f;
        button.colors = colours;

        Label(rect, "Label", text, display, displayFlat, dimensions.y * 0.5f, Ink, TextAlignmentOptions.Center,
              Middle, Vector2.zero, dimensions);

        return button;
    }

    static TMP_InputField InputField(Transform parent, string name, string placeholder, float size, Vector2 anchor,
                                     Vector2 position, Vector2 dimensions, bool boxed)
    {
        GameObject go = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
        go.name = name;
        go.transform.SetParent(parent, false);
        SetLayer(go.transform, parent.gameObject.layer);

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;

        Image background = go.GetComponent<Image>();
        background.sprite = null;
        background.color = boxed ? new Color(0f, 0f, 0f, 0.45f) : new Color(0f, 0f, 0f, 0f);

        // An underline rather than a box outline - a field you type into, not a button.
        Panel(rect, "Underline", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
              new Vector2(0f, 3f), Banana).GetComponent<Image>().raycastTarget = false;

        TMP_InputField field = go.GetComponent<TMP_InputField>();

        // First - the fontAsset setter resets both text components to the font's default
        // material, which would silently drop the outline preset assigned below.
        field.fontAsset = display;
        field.pointSize = size;

        RectTransform area = field.textViewport;
        area.offsetMin = new Vector2(boxed ? 18f : 2f, 6f);
        area.offsetMax = new Vector2(-12f, -6f);

        foreach (TMP_Text t in new[] { field.textComponent, (TMP_Text)field.placeholder })
        {
            t.font = display;
            t.fontSharedMaterial = displayInk;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.MidlineLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
        }

        field.textComponent.color = Color.white;
        ((TMP_Text)field.placeholder).text = placeholder;
        ((TMP_Text)field.placeholder).color = new Color(1f, 1f, 1f, 0.3f);
        field.caretColor = Banana;
        field.customCaretColor = true;
        field.selectionColor = new Color(Banana.r, Banana.g, Banana.b, 0.35f);

        return field;
    }

    /// A scrolling column of rows, for the lobby browser - more rooms than fit just scroll.
    static Transform ScrollList(Transform parent, string name, Vector2 position, Vector2 dimensions)
    {
        RectTransform root = Rect(parent, name, TopLeft, TopLeft, TopLeft, position, dimensions);
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;

        RectTransform viewport = Rect(root, "Viewport", Vector2.zero, Vector2.one, TopLeft, Vector2.zero, Vector2.zero);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.offsetMin = new Vector2(20f, 12f);
        viewport.offsetMax = new Vector2(-20f, 0f);

        RectTransform content = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                                     Vector2.zero, new Vector2(0f, 0f));
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = content;

        return content;
    }

    /// A thin accent bar down one edge of a card - the same bar the nav items light up with.
    static void Bar(RectTransform card, Color colour, bool left)
    {
        Vector2 edge = left ? new Vector2(0f, 0.5f) : new Vector2(1f, 0.5f);
        RectTransform bar = Panel(card, "AccentBar", new Vector2(edge.x, 0f), new Vector2(edge.x, 1f), edge,
                                  Vector2.zero, new Vector2(6f, 0f), colour);
        bar.GetComponent<Image>().raycastTarget = false;
    }

    static RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                               Vector2 position, Vector2 size, Color colour)
    {
        RectTransform rect = Rect(parent, name, anchorMin, anchorMax, pivot, position, size);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        return rect;
    }

    static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                              Vector2 position, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font, Material material, float size,
                          Color colour, TextAlignmentOptions alignment, Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        RectTransform rect = Rect(parent, name, anchor, anchor, anchor, position, dimensions);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.fontSharedMaterial = material;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = text;
        return label;
    }

    // =====================================================================================
    // style
    // =====================================================================================

    static void LoadStyle()
    {
        display = FindFont("Anton");
        body = FindFont("Jersey10");

        // Shared presets rather than a material per label (which is what HudBuilder does) - one
        // material to tweak restyles every menu label in that font at once.
        displayInk = Preset(display, "Menu Ink", 0.2f);
        bodyInk = Preset(body, "Menu Ink", 0.3f);
        displayFlat = display.material;
    }

    /// The HUD's own text treatment (HudBuilder.Text): a hard outline in the same near-black ink
    /// as the world's toon outline, plus a soft underlay behind the whole glyph so it separates
    /// from a busy background rather than just tracing round it. Thinner than the HUD's, because
    /// menu text is several times larger and the outline scales with it.
    /// The menu's shared ink preset for a font - SettingsMenuBuilder uses the same ones, so the
    /// settings screen's text is drawn exactly like the menu's.
    public static Material InkPreset(TMP_FontAsset font) =>
        Preset(font, "Menu Ink", font != null && font.name.Contains("Anton") ? 0.2f : 0.3f);

    static Material Preset(TMP_FontAsset font, string suffix, float outline)
    {
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(font));
        string path = $"{folder}/{font.name} - {suffix}.mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(font.material);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = font.material.shader;
        material.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
        material.SetColor(ShaderUtilities.ID_OutlineColor, Ink);
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.85f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.25f);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.4f);
        EditorUtility.SetDirty(material);

        return material;
    }

    static TMP_FontAsset FindFont(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/Fonts" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // The font asset itself, not a material preset sitting next to it.
            if (path.Contains(name) && path.EndsWith("SDF.asset"))
                return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        return TMP_Settings.defaultFontAsset;
    }

    // =====================================================================================
    // plumbing
    // =====================================================================================

    static void Wire(Object target, string field, Object value)
    {
        SerializedObject so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void Wire(Object target, string field, Object[] values)
    {
        SerializedObject so = new SerializedObject(target);
        SerializedProperty list = so.FindProperty(field);
        list.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t)
            SetLayer(child, layer);
    }

    static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root;
        }

        return null;
    }

    static void DestroyRoot(Scene scene, string name, System.Func<GameObject, bool> confirm)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name && confirm(root))
                Object.DestroyImmediate(root);
        }
    }

    static Transform FindChild(Transform root, System.Func<Transform, bool> match)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (match(t))
                return t;
        }

        return null;
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
