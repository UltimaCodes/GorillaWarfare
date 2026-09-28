using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;

// Into the sandbox from the real menu and back out again, recording what is on screen every frame.
//
// Reported by players: "when leaving sandbox or when joining sandbox it shows errors for a single
// frame and then what you intend to happen happens". A single frame is exactly what nobody can
// check by eye, so this writes down which menu screen was open, and every error logged, on every
// frame of both trips.
//
// Starts from the real menu with a real connection, because the flash is about what happens to
// that connection - the sandbox drops it to go offline and picks it back up on the way out.
// Joining the lobby is read-only: nothing is created or listed on the server.
//
// Lives in Editor/ on purpose - it runs in play mode, but it must never end up in a build.
public static class SandboxFlowCheck
{
    const string Flag = "GorillaWarfare.SandboxFlowCheck";

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
        new GameObject("~SandboxFlowCheck").AddComponent<SandboxFlowRunner>();
    }
}

/// Runs last in the frame, so what it records is what got drawn.
[DefaultExecutionOrder(32000)]
public class SandboxFlowRunner : MonoBehaviour
{
    const float StepTimeout = 25f;

    readonly StringBuilder log = new StringBuilder();
    readonly List<string> errors = new List<string>();
    int failures;
    string phase = "boot";
    string lastState;
    int errorScreenFrames;

    void Check(bool ok, string label, string detail)
    {
        log.AppendLine($"  {(ok ? "ok  " : "FAIL")}  {label,-52} {detail}");
        if (!ok)
            failures++;
    }

    void OnEnable() => Application.logMessageReceived += OnLog;
    void OnDisable() => Application.logMessageReceived -= OnLog;

    void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add($"[{phase} f{Time.frameCount}] {type}: {message}");
    }

    // Only the changes, so a timeline of a few hundred frames reads as a dozen lines.
    void LateUpdate()
    {
        string screens = OpenScreens();
        string state = $"scene {SceneManager.GetActiveScene().buildIndex} | {screens} | "
                       + $"{PhotonNetwork.NetworkClientState}{(PhotonNetwork.OfflineMode ? " offline" : "")}";

        if (state != lastState)
        {
            log.AppendLine($"  ..    [{phase} f{Time.frameCount}] {state}");
            lastState = state;
        }

        if (phase != "boot" && screens.Contains("error"))
            errorScreenFrames++;
    }

    static string OpenScreens()
    {
        List<string> open = new List<string>();

        foreach (Menu menu in FindObjectsByType<Menu>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (menu.gameObject.activeInHierarchy)
                open.Add(menu.menuName);
        }

        open.Sort();
        return open.Count > 0 ? string.Join(",", open) : "no menu";
    }

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);

        // Batch mode shares the Editor's own PlayerPrefs - same isolation as the probe.
        GameSettings.UsePrefsNamespace("gw_sandboxcheck_");
        KeyBinds.UsePrefsNamespace("gw_sandboxcheck_bind_");
        PlayerWallet.UsePrefsNamespace("gw_sandboxcheck_wallet_");
        AudioListener.volume = 0f;

        yield return RunCheck();

        GameSettings.ResetAll();
        PlayerPrefs.DeleteKey("gw_sandboxcheck_wallet_Tokens");
        PlayerPrefs.Save();

        if (PhotonNetwork.IsConnected)
            PhotonNetwork.Disconnect();

        Debug.Log("[sandbox] check\n" + log);
        Debug.Log(failures == 0 ? "[sandbox] ===== ALL PASS =====" : $"[sandbox] {failures} FAILURES");

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    IEnumerator RunCheck()
    {
        // ---- the menu, connected ----
        yield return Until(() => OpenScreens() == "title", "reach the title screen");
        bool online = OpenScreens() == "title" && !PhotonNetwork.OfflineMode;
        Check(online, "the menu connects and reaches the title", OpenScreens());

        if (!online)
            yield break;

        SettingsMenu settings = SettingsMenu.Instance;
        Check(settings != null, "the settings screen exists in the menu", settings != null ? "yes" : "missing");

        if (settings == null)
            yield break;

        // ---- in ----
        phase = "enter";
        errors.Clear();
        errorScreenFrames = 0;

        settings.EnterSandbox();

        yield return Until(() => MapRegistry.InMap
                                 && PlayerController.Local != null, "arrive in the sandbox");

        Check(Sandbox.Active && PlayerController.Local != null, "the sandbox loads with a player in it",
              $"active {Sandbox.Active}, scene {SceneManager.GetActiveScene().buildIndex}");
        Check(errorScreenFrames == 0, "entering never shows the error screen", $"{errorScreenFrames} frames");
        Check(errors.Count == 0, "entering logs no errors", errors.Count == 0 ? "none" : errors[0]);
        foreach (string e in errors)
            log.AppendLine($"        {e}");

        // A moment in, the way a person would be.
        yield return Wait(1.5f);

        // ---- out ----
        phase = "leave";
        errors.Clear();
        errorScreenFrames = 0;

        SettingsMenu.Instance.LeaveToMenu();

        yield return Until(() => SceneManager.GetActiveScene().buildIndex == 0 && OpenScreens() == "title",
                           "back at the title screen");

        Check(SceneManager.GetActiveScene().buildIndex == 0 && OpenScreens() == "title",
              "leaving lands back on the title", OpenScreens());
        Check(!Sandbox.Active && !PhotonNetwork.OfflineMode, "leaving ends offline mode",
              $"active {Sandbox.Active}, offline {PhotonNetwork.OfflineMode}");
        Check(errorScreenFrames == 0, "leaving never shows the error screen", $"{errorScreenFrames} frames");
        Check(errors.Count == 0, "leaving logs no errors", errors.Count == 0 ? "none" : errors[0]);
        foreach (string e in errors)
            log.AppendLine($"        {e}");

        // The fallback region is for a server that can't be reached, not for coming back from
        // the sandbox - landing on it would mean silently not seeing your friends' rooms.
        string region = PhotonNetwork.CloudRegion ?? "";
        string fixedRegion = PhotonNetwork.PhotonServerSettings.AppSettings.FixedRegion ?? "";
        Check(fixedRegion.Length == 0 || region.StartsWith(fixedRegion),
              "back on the fixed region afterwards", $"'{region}' (fixed '{fixedRegion}')");
    }

    IEnumerator Until(System.Func<bool> condition, string what)
    {
        float start = Time.realtimeSinceStartup;

        while (!condition())
        {
            if (Time.realtimeSinceStartup - start > StepTimeout)
            {
                log.AppendLine($"  ..    timed out waiting to {what}");
                yield break;
            }

            yield return null;
        }
    }

    static IEnumerator Wait(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
            yield return null;
    }
}
