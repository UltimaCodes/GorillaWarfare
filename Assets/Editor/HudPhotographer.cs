using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Screenshots the real in-match HUD with representative sample data on it - a hit, a kill
/// callout, a feed line, a damage bearing - rather than the empty warmup state it builds with.
///
/// The HUD is a Screen Space - Overlay canvas, which Camera.Render never draws (see
/// PlayModeProbe's own note by CaptureEveryWeapon) and which ScreenCapture can only reach through
/// a real end-of-frame - one batch mode never delivers, hanging the whole process rather than
/// producing a screenshot. So this runs without -batchmode: a real, visible Editor window, the
/// same offline-room boot PlayModeProbe already uses to get a real local player without a
/// network, and ScreenCapture once the frame has actually settled.
///
/// Lives in Editor/ for the same reason PlayModeProbe does: it runs in play mode and must never
/// ship in a build.
/// </summary>
public static class HudPhotographer
{
    const string Flag = "GorillaWarfare.HudPhotographer";

    public static string OutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "hud-check.png");

    [MenuItem("Tools/Gorilla Warfare/Photograph the HUD")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);
        new GameObject("~HudPhotographerRunner").AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);

            if (PhotonNetwork.IsConnected)
            {
                PhotonNetwork.Disconnect();
                yield return new WaitUntil(() => !PhotonNetwork.IsConnected);
            }

            PhotonNetwork.OfflineMode = true;
            PhotonNetwork.NickName = "HudCheck";

            if (!PhotonNetwork.CreateRoom("hudcheck", new Photon.Realtime.RoomOptions { MaxPlayers = 8 }))
            {
                Debug.LogError("[hudshot] CreateRoom refused");
                EditorApplication.Exit(1);
                yield break;
            }

            yield return new WaitUntil(() => PhotonNetwork.InRoom);

            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { MatchState.ModeKey, (int)MatchMode.Deathmatch } });

            PhotonNetwork.LoadLevel(1);
            yield return new WaitUntil(() =>
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 1);

            PlayerController player = null;
            yield return new WaitUntil(() =>
            {
                player = Object.FindFirstObjectByType<PlayerController>();
                return player != null;
            });

            // A couple of frames for Start/Awake fallout - GameHud.Bind and the weapon build
            // both land a frame after spawn, same timing PlayModeProbe waits out.
            yield return null;
            yield return null;

            GameHud hud = GameHud.Instance;

            if (hud == null)
            {
                Debug.LogError("[hudshot] no GameHud in the scene");
                EditorApplication.Exit(1);
                yield break;
            }

            // Representative sample state - an idle HUD doesn't show whether the feed, the
            // damage numbers or a fresh kill callout still read against the new frame/backers.
            hud.ShowHit(true);
            hud.ShowDamage(player.transform.position + player.transform.forward * 3f + Vector3.up, 42f, true);
            hud.ShowDamage(player.transform.position + player.transform.forward * 2.5f, 18f, false);
            hud.ShowDamageFrom(player.transform.position - player.transform.right * 6f);
            hud.ShowKill(2, 3);
            hud.ShowCombo(4);

            MatchState.Feed.Add(new MatchState.FeedEntry
            {
                kind = MatchState.FeedKind.Kill, actor = "You", subject = "Rival",
                weapon = "Shotgun", headshot = true, involvesYou = true, at = Time.unscaledTime,
            });
            MatchState.Feed.Add(new MatchState.FeedEntry
            {
                kind = MatchState.FeedKind.Join, actor = "Newcomer", at = Time.unscaledTime,
            });

            yield return null;
            yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(OutputPath);

            // CaptureScreenshot writes out at the *next* end of frame, not this one.
            yield return new WaitForEndOfFrame();
            yield return null;

            Debug.Log($"[hudshot] -> {OutputPath}");
            EditorApplication.Exit(0);
        }
    }
}
