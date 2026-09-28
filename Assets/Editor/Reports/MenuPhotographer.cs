using System.Collections;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Photographs every main-menu screen, in play mode, with the live backdrop behind it - so a
/// person (and I) can look at the real thing instead of trusting a layout. Output:
/// Logs/menu-shots/*.png at 1920x1080, the menu's reference resolution.
///
/// The menu's canvas is Screen Space - Overlay, which Camera.Render never draws, so for each shot
/// every overlay canvas is switched to a temporary UI camera layered over the backdrop camera
/// and put straight back afterwards. Runs in batch mode (without -nographics): Photon is put in
/// offline mode first, so nothing connects and the lobby shot is a real offline room.
///
/// Unity -batchmode -projectPath . -executeMethod MenuPhotographer.Run   (no -quit)
/// </summary>
public static class MenuPhotographer
{
    const string Flag = "GorillaWarfare.MenuPhotographer";
    const string PrefsPrefix = "gw_menushot_";

    /// Leaves no photographer keys behind - every reset here only touches its own namespace.
    static void CleanUp()
    {
        GameSettings.ResetAll();
        PlayerPrefs.DeleteKey(PrefsPrefix + "wallet_Tokens");
        PlayerPrefs.Save();
    }
    const int Width = 1920;
    const int Height = 1080;

    static string ShotFolder =>
        Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "menu-shots");

    /// The live lights and ambient setup - PlayModeProbe logs the game scene's with this too.
    public static string LightingReport() => Runner.LightingReport();

    [MenuItem("Tools/Gorilla Warfare/Photograph the main menu")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// A straight-down view of the menu backdrop with a 10 m grid of markers, plus the sun's
    /// direction - for choosing where the menu camera stands: in the sun, with open sky behind the
    /// menu column. Edit mode, no play needed. Output: Logs/menu-shots/topdown.png.
    /// </summary>
    [MenuItem("Tools/Gorilla Warfare/Photograph the menu backdrop from above")]
    public static void TopDown()
    {
        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);

        Light sun = RenderSettings.sun;
        Debug.Log($"[menushot] sun forward {(sun != null ? sun.transform.forward.ToString("F2") : "none")}, "
                  + $"rotation {(sun != null ? sun.transform.eulerAngles.ToString("F0") : "-")}");

        GameObject host = new GameObject("~TopDown");
        Camera cam = host.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 66f;
        cam.transform.position = new Vector3(-1f, 120f, -1f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.farClipPlane = 300f;

        // Red posts every 10 m, taller every 50 m, so positions can be read off the picture.
        List<GameObject> markers = new List<GameObject>();
        for (int x = -60; x <= 60; x += 10)
        {
            for (int z = -60; z <= 60; z += 10)
            {
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bool major = x % 50 == 0 && z % 50 == 0;
                post.transform.position = new Vector3(x, 30f, z);
                post.transform.localScale = Vector3.one * (major ? 2.2f : (x == 0 || z == 0 ? 1.4f : 0.8f));
                post.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = x == 0 && z == 0 ? Color.magenta : Color.red };
                markers.Add(post);
            }
        }

        MenuBackdropCamera menuCam = Object.FindFirstObjectByType<MenuBackdropCamera>();
        if (menuCam != null)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.transform.position = menuCam.transform.position + Vector3.up * 30f;
            eye.transform.localScale = Vector3.one * 3f;
            eye.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.yellow };
            markers.Add(eye);
            Debug.Log($"[menushot] menu camera at {menuCam.transform.position:F1}, forward {menuCam.transform.forward:F2}");
        }

        RenderTexture target = new RenderTexture(1200, 1200, 24);
        cam.targetTexture = target;
        cam.Render();

        RenderTexture.active = target;
        Texture2D shot = new Texture2D(1200, 1200, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, 1200, 1200), 0, 0);
        shot.Apply();
        RenderTexture.active = null;

        Directory.CreateDirectory(ShotFolder);
        File.WriteAllBytes(Path.Combine(ShotFolder, "topdown.png"), shot.EncodeToPNG());

        foreach (GameObject m in markers)
            Object.DestroyImmediate(m);
        Object.DestroyImmediate(host);
        Debug.Log("[menushot] topdown written");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);

        // Before anyone's Start - so Launcher's connect goes nowhere and the lobby is offline.
        PhotonNetwork.OfflineMode = true;

        // Its own prefs namespace, so shooting the menu can't touch real settings - and not the
        // probe's either: a GW_MENU_SHADERS=Off run once left post switched off for the next probe.
        GameSettings.UsePrefsNamespace(PrefsPrefix);
        KeyBinds.UsePrefsNamespace(PrefsPrefix + "bind_");
        PlayerWallet.UsePrefsNamespace(PrefsPrefix + "wallet_");

        new GameObject("~MenuPhotographer").AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        readonly List<string> taken = new List<string>();
        Camera heldCamera;

        IEnumerator Start()
        {
            AudioListener.volume = 0f;
            float started = Time.realtimeSinceStartup;

            // Launcher's own ConnectUsingSettings switches offline mode back off and starts a real
            // connection - which could land "joined lobby" mid-shot and flip the screen, and would
            // make the lobby shot's room a real, listed one. Dropped and forced offline first.
            if (PhotonNetwork.IsConnected || PhotonNetwork.NetworkClientState != Photon.Realtime.ClientState.Disconnected)
            {
                PhotonNetwork.Disconnect();
                float connectDeadline = Time.realtimeSinceStartup + 5f;
                while (PhotonNetwork.IsConnected && Time.realtimeSinceStartup < connectDeadline)
                    yield return null;
            }
            PhotonNetwork.OfflineMode = true;

            // The gorilla, the grass and the post stack all build over the first frames.
            yield return new WaitForSecondsRealtime(1.5f);

            MenuManager menus = MenuManager.Instance;
            if (menus == null)
            {
                Debug.LogError("[menushot] no MenuManager - is the menu built?");
                CleanUp();
                EditorApplication.Exit(1);
                yield break;
            }

            yield return Shot("loading", "loading");
            yield return Shot("title", "title");

            // GW_MENU_CANDIDATES="x,y,z,yaw;x,y,z,yaw;..." - the title screen from each of those
            // camera spots, gorilla re-placed the same way the builder places it, to pick a
            // composition by looking rather than by guessing.
            string candidates = System.Environment.GetEnvironmentVariable("GW_MENU_CANDIDATES");
            if (!string.IsNullOrEmpty(candidates))
            {
                yield return Candidates(candidates);
                Debug.Log($"[menushot] {taken.Count} shots:\n  " + string.Join("\n  ", taken));
                CleanUp();
                EditorApplication.Exit(0);
                yield break;
            }
            yield return Shot("find room", "find-empty");
            yield return FakeRooms();
            yield return Shot("find room", "find-rooms");
            yield return Shot("create room", "create");

            ShowError("Could not join room: Game full");
            yield return Shot(null, "error");

            // A real room, so the player list, mode selector and colour picker are all live.
            PhotonNetwork.NickName = "Ryaan";
            PhotonNetwork.CreateRoom("JUNGLE HQ", new Photon.Realtime.RoomOptions { MaxPlayers = 12 });
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!PhotonNetwork.InRoom && Time.realtimeSinceStartup < deadline)
                yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot(null, "lobby");

            // The same lobby with the zoo picked - the backdrop follows the map (MenuBackdrop).
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { MapRegistry.RoomKey, "zoo" } });
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot(null, "lobby-zoo");
            PhotonNetwork.CurrentRoom.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { MapRegistry.RoomKey, MapRegistry.Default.key } });
            yield return new WaitForSecondsRealtime(0.5f);

            if (SettingsMenu.Instance != null)
            {
                SettingsMenu.Instance.Open();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot(null, "settings");
                SettingsMenu.Instance.Close();
            }

            // The crate shop, and a crate opened for real (the spin runs 5.6s).
            if (CrateOpeningScreen.Instance != null)
            {
                PlayerPrefs.SetInt(PrefsPrefix + "wallet_Tokens", 1000);
                PlayerWallet.UsePrefsNamespace(PrefsPrefix + "wallet_");

                CrateOpeningScreen.Instance.Open();
                yield return Shot(null, "crates");

                typeof(CrateOpeningScreen).GetMethod("TryOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(CrateOpeningScreen.Instance, new object[] { CrateInfo.Ripe });
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot(null, "crates-spinning");
                yield return new WaitForSecondsRealtime(4.5f);
                yield return Shot(null, "crates-result");

                CrateOpeningScreen.Instance.Close();
            }

            Debug.Log($"[menushot] {taken.Count} shots in {Time.realtimeSinceStartup - started:F1}s:\n  " + string.Join("\n  ", taken));
            CleanUp();
            EditorApplication.Exit(0);
        }

        /// Two example rows in the browser - offline mode lists no rooms, so the table would
        /// otherwise only ever be photographed empty.
        IEnumerator FakeRooms()
        {
            Launcher launcher = Launcher.Instance;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Transform content = typeof(Launcher).GetField("roomListContent", flags)?.GetValue(launcher) as Transform;
            GameObject prefab = typeof(Launcher).GetField("roomListItemPrefab", flags)?.GetValue(launcher) as GameObject;
            GameObject empty = typeof(Launcher).GetField("noRoomsMessage", flags)?.GetValue(launcher) as GameObject;

            if (content == null || prefab == null)
                yield break;

            string[,] rows = { { "JUNGLE HQ", "DEATHMATCH", "3/12" }, { "BANANA REPUBLIC", "GUN GAME", "7/12" },
                               { "SILVERBACKS ONLY", "TEAM DEATHMATCH", "11/12" } };

            for (int i = 0; i < rows.GetLength(0); i++)
            {
                GameObject row = Instantiate(prefab, content);
                TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text t in texts)
                {
                    if (t.name == "Name") t.text = rows[i, 0];
                    if (t.name == "Mode") t.text = rows[i, 1];
                    if (t.name == "Players") t.text = rows[i, 2];
                }
            }

            if (empty != null)
                empty.SetActive(false);

            yield return null;
        }

        IEnumerator Candidates(string spec)
        {
            MenuBackdropCamera drift = FindFirstObjectByType<MenuBackdropCamera>();
            MenuGorilla gorilla = FindFirstObjectByType<MenuGorilla>();
            if (drift == null)
                yield break;

            Debug.Log("[menushot] lighting:\n" + LightingReport());

            // Held still, so every candidate is shot from exactly its own pose. Disabling it also
            // unregisters it as MenuBackdropCamera.Current, so Shot is handed the camera directly -
            // the first version of this rendered six black frames for exactly that reason.
            heldCamera = drift.GetComponent<Camera>();
            drift.enabled = false;
            Transform cam = drift.transform;
            string[] spots = spec.Split(';');

            for (int i = 0; i < spots.Length; i++)
            {
                string[] v = spots[i].Split(',');
                if (v.Length < 4)
                    continue;

                float x = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture);
                float y = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture);
                float z = float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture);
                float yaw = float.Parse(v[3], System.Globalization.CultureInfo.InvariantCulture);

                float ground = 0f;
                if (Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out RaycastHit hit, 120f, 1))
                    ground = hit.point.y;

                cam.position = new Vector3(x, ground + y, z);
                cam.rotation = Quaternion.Euler(-2f, yaw, 0f);

                if (gorilla != null)
                {
                    Vector3 at = cam.position + cam.forward * 4.2f + cam.right * 1.7f;
                    if (Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out RaycastHit feet, 60f, 1))
                        at.y = feet.point.y;
                    gorilla.transform.position = at;
                    gorilla.transform.rotation = Quaternion.LookRotation(
                        Vector3.ProjectOnPlane(cam.position - at, Vector3.up), Vector3.up) * Quaternion.Euler(0f, -28f, 0f);
                }

                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("title", $"cand-{i}");
            }
        }

        /// Every live light and the ambient setup, for comparing the menu's lighting with the game's.
        public static string LightingReport()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // The grass's own inputs as they are right now, not as the assets say they should be.
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (GrassComputeScript g in FindObjectsByType<GrassComputeScript>(FindObjectsSortMode.None))
            {
                Material m = typeof(GrassComputeScript).GetField("m_InstantiatedMaterial", flags)?.GetValue(g) as Material;
                List<GrassData> data = g.SetGrassPaintedDataList;
                string first = data != null && data.Count > 0 ? $"{data[0].color:F2} len {data[0].length:F2} n {data[0].normal:F2}" : "none";
                sb.AppendLine($"    grass '{g.name}' init={g.IsInitialized} points={data?.Count} visible={g.VisibleCount} "
                              + $"material={(m != null ? m.name : "null")} "
                              + $"top={(m != null ? m.GetColor("_TopTint").ToString("F2") : "-")} bottom={(m != null ? m.GetColor("_BottomTint").ToString("F2") : "-")} "
                              + $"shader={(m != null ? m.shader.name : "-")} first point colour {first}");
            }
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                sb.AppendLine($"    light '{l.name}' {l.type} enabled={l.enabled && l.gameObject.activeInHierarchy} intensity={l.intensity} "
                              + $"colour=#{ColorUtility.ToHtmlStringRGB(l.color)} mode={l.renderMode} bake={l.lightmapBakeType} "
                              + $"mask={l.cullingMask} shadows={l.shadows} forward={l.transform.forward:F2}");
            }

            UnityEngine.Rendering.SphericalHarmonicsL2 sh = RenderSettings.ambientProbe;
            sb.AppendLine($"    ambient mode={RenderSettings.ambientMode} intensity={RenderSettings.ambientIntensity} "
                          + $"sky=#{ColorUtility.ToHtmlStringRGB(RenderSettings.ambientSkyColor)} "
                          + $"probe L0=({sh[0, 0]:F3}, {sh[1, 0]:F3}, {sh[2, 0]:F3}) skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")} "
                          + $"sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "none")}");
            return sb.ToString();
        }

        static void ShowError(string message)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(Launcher).GetMethod("ShowError", flags)?.Invoke(Launcher.Instance, new object[] { message });
        }

        IEnumerator Shot(string menu, string file)
        {
            if (menu != null && MenuManager.Instance != null)
                MenuManager.Instance.OpenMenu(menu);

            // Layout groups, fitters and the hover eases settle over a couple of frames.
            yield return new WaitForSecondsRealtime(0.35f);

            Camera world = heldCamera != null ? heldCamera : MenuBackdropCamera.Current;
            RenderTexture target = new RenderTexture(Width, Height, 24);

            GameObject uiHost = new GameObject("~ShotUiCamera");

            // Far below the arena with a short far plane, so the only thing this camera can see is
            // the canvases a metre in front of it. The settings canvas isn't on the UI layer, so the
            // mask below includes Default - and from the world origin that drew the arena a second
            // time at ground height, without post, over the whole top half of every shot.
            uiHost.transform.position = new Vector3(0f, -10000f, 0f);

            Camera ui = uiHost.AddComponent<Camera>();
            ui.clearFlags = CameraClearFlags.Depth;
            ui.farClipPlane = 5f;
            ui.cullingMask = 1 << LayerMask.NameToLayer("UI");
            ui.depth = 100f;
            ui.targetTexture = target;

            // Every overlay canvas onto the UI camera for the length of the shot - the menu's own,
            // plus the settings screen and crate shop RoomManager instantiated.
            List<Canvas> switched = new List<Canvas>();
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = ui;
                canvas.planeDistance = 1f + switched.Count * 0.01f;
                switched.Add(canvas);

                // Whatever layer each canvas is on - the settings screen isn't on UI, and a UI-only
                // mask photographed the lobby behind it as if it had never opened.
                ui.cullingMask |= 1 << canvas.gameObject.layer;
            }

            Canvas.ForceUpdateCanvases();
            yield return null;
            Canvas.ForceUpdateCanvases();

            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previousActive;

            if (world != null)
            {
                int mask = world.cullingMask;
                world.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
                world.targetTexture = target;
                world.Render();
                world.targetTexture = null;
                world.cullingMask = mask;
            }

            ui.Render();

            RenderTexture.active = target;
            Texture2D shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();
            RenderTexture.active = previousActive;

            Directory.CreateDirectory(ShotFolder);
            string path = Path.Combine(ShotFolder, file + ".png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            taken.Add(path);

            foreach (Canvas canvas in switched)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
            }

            Destroy(shot);
            Destroy(uiHost);
            target.Release();
            Destroy(target);
        }
    }
}
