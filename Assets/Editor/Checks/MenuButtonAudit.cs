using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Photon.Pun;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Clicks every control on every menu screen, the way the EventSystem would, and reports the ones
/// that can't work.
///
/// Reported by players: "a lot of buttons don't work, especially in the loot crate section". A
/// button that looks fine and does nothing fails in one of three ways, and all three are checkable
/// without a person: something else is on top of it (the click lands on the thing in front), it's
/// off the screen, or nothing is listening to it. For each visible, interactable control this asks
/// the EventSystem what a click at the control's centre would actually reach, and counts its
/// listeners.
///
/// Play mode, offline (same setup as MenuPhotographer), its own prefs namespace.
/// Unity -batchmode -projectPath . -executeMethod MenuButtonAudit.Run   (no -quit)
/// </summary>
public static class MenuButtonAudit
{
    const string Flag = "GorillaWarfare.MenuButtonAudit";
    public const string PrefsPrefix = "gw_buttonaudit_";

    [MenuItem("Tools/Gorilla Warfare/Audit the menu buttons")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);

        // Before anyone's Start - so Launcher's connect goes nowhere.
        PhotonNetwork.OfflineMode = true;
        GameSettings.UsePrefsNamespace(PrefsPrefix);
        KeyBinds.UsePrefsNamespace(PrefsPrefix + "bind_");
        PlayerWallet.UsePrefsNamespace(PrefsPrefix + "wallet_");
        SkinInventory.UsePrefsNamespace(PrefsPrefix + "skins_");

        new GameObject("~MenuButtonAudit").AddComponent<MenuButtonAuditRunner>();
    }
}

public class MenuButtonAuditRunner : MonoBehaviour
{
    readonly StringBuilder log = new StringBuilder();
    int problems;
    int warnings;
    int audited;

    // The menu's canvases are drawn through this camera into a texture of a chosen size, so the
    // audit can run at a real screen shape - batch mode's own screen is 640x480, which reports
    // 4:3 overflow nobody at 16:9 would ever see. Same trick MenuPhotographer uses to photograph.
    Camera virtualScreen;
    bool strict;
    string size;

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        AudioListener.volume = 0f;

        if (PhotonNetwork.IsConnected || PhotonNetwork.NetworkClientState != Photon.Realtime.ClientState.Disconnected)
        {
            PhotonNetwork.Disconnect();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (PhotonNetwork.IsConnected && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
        PhotonNetwork.OfflineMode = true;

        yield return new WaitForSecondsRealtime(1.5f);

        log.AppendLine("  ..    event system " + (EventSystem.current != null ? EventSystem.current.name : "MISSING"));

        if (MenuManager.Instance == null)
        {
            Finish("no MenuManager");
            yield break;
        }

        UseVirtualScreen();

        // 16:9 is what nearly everyone plays at; 4:3 is the narrowest shape a window is likely to
        // be. Both fail on a problem - 4:3 was report-only until the menu's Canvas Scaler went to
        // Expand (2026-09-27) and it came up clean, so now it guards that.
        yield return Pass(1920, 1080, true);
        yield return Pass(1440, 1080, true);

        Finish(null);
    }

    void UseVirtualScreen()
    {
        GameObject host = new GameObject("~AuditScreen");
        DontDestroyOnLoad(host);

        // Far below the map with a short far plane, so it sees the canvases and nothing else.
        host.transform.position = new Vector3(0f, -10000f, 0f);
        virtualScreen = host.AddComponent<Camera>();
        virtualScreen.clearFlags = CameraClearFlags.SolidColor;
        virtualScreen.farClipPlane = 5f;
        virtualScreen.cullingMask = 0;

        int i = 0;
        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                continue;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = virtualScreen;
            canvas.planeDistance = 1f + (i++) * 0.01f;
            virtualScreen.cullingMask |= 1 << canvas.gameObject.layer;
        }
    }

    IEnumerator Pass(int width, int height, bool failsOnProblems)
    {
        strict = failsOnProblems;
        size = $"{width}x{height}";

        RenderTexture previous = virtualScreen.targetTexture;
        virtualScreen.targetTexture = new RenderTexture(width, height, 24);
        if (previous != null)
            previous.Release();

        log.AppendLine($"  ..    ---- {size}{(strict ? "" : " (reported, not failed)")} ----");

        MenuManager menus = MenuManager.Instance;

        foreach (string screen in new[] { "title", "find room", "create room" })
        {
            menus.OpenMenu(screen);
            yield return Settle();
            Audit(screen, null);
        }

        typeof(Launcher).GetMethod("ShowError", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(Launcher.Instance, new object[] { "Could not join room: Game full" });
        yield return Settle();
        Audit("error", null);

        // ---- crates ----
        menus.OpenMenu("title");
        PlayerPrefs.SetInt(MenuButtonAudit.PrefsPrefix + "wallet_Tokens", 1000);
        PlayerWallet.UsePrefsNamespace(MenuButtonAudit.PrefsPrefix + "wallet_");

        CrateOpeningScreen crates = CrateOpeningScreen.Instance;

        if (crates != null)
        {
            // Through the title screen's own button, so the way in is exercised as well.
            Button way = FindButtonWith<OpenCrateShopButton>();
            if (way != null)
                way.onClick.Invoke();
            else
                crates.Open();

            yield return Settle();
            Audit("crates - pick a crate", crates.transform);

            // Open the cheapest one for real and wait out the spin.
            MethodInfo tryOpen = typeof(CrateOpeningScreen).GetMethod("TryOpen", BindingFlags.NonPublic | BindingFlags.Instance);
            tryOpen?.Invoke(crates, new object[] { CrateInfo.Rotten });

            // The chest, the reel and the reveal take about nine seconds - waited out, not guessed.
            float resultDeadline = Time.realtimeSinceStartup + 20f;
            while (!crates.ShowingResult && Time.realtimeSinceStartup < resultDeadline)
                yield return null;
            yield return new WaitForSecondsRealtime(1.2f);
            if (!crates.ShowingResult)
                Report("crates - the result", "the result never came up");
            Audit("crates - the result", crates.transform);

            crates.Close();
            yield return Settle();
        }
        else
        {
            Report("crates", "there is no crate shop");
        }

        // ---- the inventory, with something in it from the crate above ----
        menus.OpenMenu("title");
        InventoryScreen inventory = InventoryScreen.Instance;
        if (inventory != null)
        {
            Button way = FindButtonWith<OpenInventoryButton>();
            if (way != null)
                way.onClick.Invoke();
            else
            {
                Report("inventory", "no INVENTORY on the title screen");
                inventory.Open();
            }

            yield return new WaitForSecondsRealtime(1f);
            Audit("inventory", inventory.transform);
            inventory.Close();
            yield return Settle();
        }
        else
        {
            Report("inventory", "there is no inventory screen");
        }

        // ---- settings, every tab ----
        menus.OpenMenu("title");

        if (SettingsMenu.Instance != null)
        {
            SettingsMenu.Instance.Open();
            yield return Settle();

            foreach (string tab in new[] { "Aim", "Audio", "Video", "Crosshair", "Keys" })
            {
                Button tabButton = FindButtonNamed("Tab" + tab);
                if (tabButton != null)
                    tabButton.onClick.Invoke();

                yield return Settle();
                Audit($"settings - {tab.ToLower()}", SettingsMenu.Instance.transform);
            }

            SettingsMenu.Instance.Close();
            yield return Settle();
        }
        else
        {
            Report("settings", "there is no settings screen");
        }

        // ---- the lobby, last - it needs a room ----
        if (!PhotonNetwork.InRoom)
        {
            PhotonNetwork.NickName = "Audit";
            PhotonNetwork.CreateRoom("AUDIT", new Photon.Realtime.RoomOptions { MaxPlayers = 12 });
            float roomDeadline = Time.realtimeSinceStartup + 10f;
            while (!PhotonNetwork.InRoom && Time.realtimeSinceStartup < roomDeadline)
                yield return null;
        }

        menus.OpenMenu("room");
        yield return Settle();
        Audit("lobby", null);
    }

    void Report(string screen, string problem)
    {
        log.AppendLine($"  {(strict ? "FAIL" : "warn")}  [{size} {screen}] {problem}");
        if (strict) problems++; else warnings++;
    }

    static IEnumerator Settle()
    {
        yield return new WaitForSecondsRealtime(0.35f);
        Canvas.ForceUpdateCanvases();
        yield return null;
    }

    static Button FindButtonWith<T>() where T : Component
    {
        foreach (T component in FindObjectsByType<T>(FindObjectsSortMode.None))
        {
            Button button = component.GetComponent<Button>();
            if (button != null && button.gameObject.activeInHierarchy)
                return button;
        }

        return null;
    }

    static Button FindButtonNamed(string name)
    {
        foreach (Button button in FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            if (button.name == name && button.gameObject.activeInHierarchy)
                return button;
        }

        return null;
    }

    /// Everything clickable on the screen that's showing. With a scope - a modal like the crate
    /// shop or the settings screen - only what's inside it: the title screen underneath a modal is
    /// meant to be unreachable, and reporting it as covered would bury the real problems.
    void Audit(string screen, Transform scope)
    {
        List<string> lines = new List<string>();
        int count = 0;
        int bad = 0;
        string fail = strict ? "FAIL" : "warn";

        EventSystem events = EventSystem.current;

        if (events == null)
        {
            Report(screen, "no EventSystem - nothing on this screen can be clicked");
            return;
        }

        List<RaycastResult> hits = new List<RaycastResult>();
        Camera eye = virtualScreen;

        foreach (Selectable control in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
        {
            if (!control.gameObject.activeInHierarchy || !control.IsActive())
                continue;

            if (scope != null && !control.transform.IsChildOf(scope))
                continue;

            RectTransform rect = control.transform as RectTransform;
            if (rect == null)
                continue;

            Canvas canvas = control.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.enabled)
                continue;

            // Anything fully transparent through a CanvasGroup isn't on screen for a person either.
            if (!VisibleThroughGroups(control.transform))
                continue;

            Vector2 point = RectTransformUtility.WorldToScreenPoint(eye, rect.TransformPoint(rect.rect.center));

            // A row scrolled out of its list is hidden by the list's mask, not broken.
            if (!InsideItsMask(control.transform, point, eye))
                continue;

            count++;
            string name = Describe(control);

            if (!control.IsInteractable())
            {
                lines.Add($"  ..    [{screen}] {name} is disabled (not interactable)");
                continue;
            }

            if (point.x < 0f || point.y < 0f || point.x > eye.pixelWidth || point.y > eye.pixelHeight)
            {
                lines.Add($"  {fail}  [{screen}] {name} is off the screen at {point:F0}");
                bad++;
                continue;
            }

            hits.Clear();
            events.RaycastAll(new PointerEventData(events) { position = point }, hits);

            GameObject top = hits.Count > 0 ? hits[0].gameObject : null;
            GameObject reached = null;

            if (top != null)
            {
                reached = control is Slider || control is Scrollbar
                    ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(top)
                    : ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
            }

            if (reached != control.gameObject)
            {
                string blocker = top == null ? "nothing - no graphic under it takes the click"
                                             : $"{Path(top.transform)} (on '{top.GetComponentInParent<Canvas>()?.rootCanvas.name}')";
                lines.Add($"  {fail}  [{screen}] {name} is covered - a click lands on {blocker}");
                bad++;
                continue;
            }

            int listeners = ListenerCount(control);

            if (listeners == 0)
            {
                lines.Add($"  {fail}  [{screen}] {name} does nothing - no listeners");
                bad++;
            }
        }

        audited += count;

        if (strict) problems += bad; else warnings += bad;

        log.AppendLine($"  {(bad == 0 ? "ok  " : fail)}  [{size} {screen}] {count} controls, {bad} broken");
        foreach (string line in lines)
            log.AppendLine("    " + line.TrimStart());
    }

    static bool InsideItsMask(Transform t, Vector2 point, Camera eye)
    {
        for (Transform at = t.parent; at != null; at = at.parent)
        {
            bool masks = at.GetComponent<RectMask2D>() != null
                         || (at.GetComponent<Mask>() is Mask mask && mask.enabled);

            if (masks && !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)at, point, eye))
                return false;
        }

        return true;
    }

    static bool VisibleThroughGroups(Transform t)
    {
        for (Transform at = t; at != null; at = at.parent)
        {
            CanvasGroup group = at.GetComponent<CanvasGroup>();
            if (group != null && group.alpha <= 0.01f)
                return false;
        }

        return true;
    }

    /// Persistent (inspector) plus runtime (AddListener) listeners on whatever event the control
    /// fires. Input fields are read by other code rather than listened to, so they always pass.
    static int ListenerCount(Selectable control)
    {
        UnityEngine.Events.UnityEventBase evt = control switch
        {
            Button b => b.onClick,
            Toggle t => t.onValueChanged,
            Slider s => s.onValueChanged,
            Scrollbar sb => sb.onValueChanged,
            TMP_Dropdown d => d.onValueChanged,
            _ => null,
        };

        if (evt == null)
            return 1;

        int count = evt.GetPersistentEventCount();

        FieldInfo callsField = typeof(UnityEngine.Events.UnityEventBase).GetField("m_Calls", BindingFlags.NonPublic | BindingFlags.Instance);
        object calls = callsField?.GetValue(evt);
        FieldInfo runtimeField = calls?.GetType().GetField("m_RuntimeCalls", BindingFlags.NonPublic | BindingFlags.Instance);

        if (runtimeField?.GetValue(calls) is System.Collections.ICollection runtime)
            count += runtime.Count;
        else
            count += 1; // can't see inside - don't call it dead on a guess

        // Something else on the same object handling the click (an EventTrigger, a custom
        // handler) counts too.
        foreach (Component other in control.GetComponents<Component>())
        {
            if (other != control && other is IPointerClickHandler && !(other is Selectable))
                count++;
        }

        return count;
    }

    static string Describe(Selectable control)
    {
        TMP_Text label = control.GetComponentInChildren<TMP_Text>();
        string text = label != null && !string.IsNullOrWhiteSpace(label.text) ? $" \"{label.text.Trim()}\"" : "";
        return $"{control.GetType().Name} {Path(control.transform)}{text}";
    }

    static string Path(Transform t)
    {
        string path = t.name;
        for (Transform at = t.parent; at != null && at.parent != null; at = at.parent)
            path = at.name + "/" + path;
        return path;
    }

    void Finish(string fatal)
    {
        if (fatal != null)
        {
            log.AppendLine($"  FAIL  {fatal}");
            problems++;
        }

        GameSettings.ResetAll();
        PlayerPrefs.DeleteKey(MenuButtonAudit.PrefsPrefix + "wallet_Tokens");
        PlayerPrefs.DeleteKey(MenuButtonAudit.PrefsPrefix + "skins_Owned");
        PlayerPrefs.DeleteKey(MenuButtonAudit.PrefsPrefix + "skins_Equipped");
        PlayerPrefs.Save();

        Debug.Log($"[buttons] audit - {audited} controls, {warnings} warnings at 4:3\n" + log);
        Debug.Log(problems == 0 ? "[buttons] ===== ALL PASS =====" : $"[buttons] {problems} FAILURES");
        EditorApplication.Exit(problems == 0 ? 0 : 1);
    }
}
