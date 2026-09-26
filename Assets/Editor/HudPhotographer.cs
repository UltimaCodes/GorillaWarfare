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

    public static string ResultsOutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "results-check.png");

    public static string SettingsOutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "settings-check.png");

    public static string CrateShopOutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "crate-shop-check.png");

    public static string CrateRevealOutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "crate-reveal-check.png");

    static T GetPrivateField<T>(object target, string field) where T : class
    {
        if (target == null)
            return null;

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        return target.GetType().GetField(field, flags)?.GetValue(target) as T;
    }

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

            // Same isolation PlayModeProbe uses - this ends a match too, which pays tokens.
            GameSettings.UsePrefsNamespace("gw_probe_");
            KeyBinds.UsePrefsNamespace("gw_probe_bind_");
            PlayerWallet.UsePrefsNamespace("gw_probe_wallet_");

            // -nographics suppresses rendering but not audio - without this, every gunshot,
            // death cry and hit sound this session's real play mode triggers comes out of
            // whatever speakers are actually attached, which is exactly what was reported:
            // "the game just runs in the background with the audio on."
            AudioListener.volume = 0f;

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

            // GW_HUD_MODE=GunGame checks the ladder specifically - it only ever shows in that
            // mode, so the default Deathmatch run never exercises it at all. Parsed by name
            // rather than a binary ternary so TeamDeathmatch (and whatever Party's minigames end
            // up named) are screenshot-able without editing this line for every new mode.
            string modeEnv = System.Environment.GetEnvironmentVariable("GW_HUD_MODE");
            MatchMode mode = !string.IsNullOrEmpty(modeEnv) && System.Enum.TryParse(modeEnv, out MatchMode parsed)
                ? parsed
                : MatchMode.Deathmatch;

            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { MatchState.ModeKey, (int)mode } });

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

            // Forces a live slide chain so the rank text shows up too - SlideChain/Exhausted are
            // both derived read-only properties (Time.time against a couple of private fields),
            // so this is the only way to make one true without actually sliding the rig.
            if (mode == MatchMode.GunGame)
            {
                PlayerMovement movement = player.GetComponent<PlayerMovement>();

                if (movement != null)
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    typeof(PlayerMovement).GetField("chain", flags)?.SetValue(movement, 4);
                    typeof(PlayerMovement).GetField("chainExpires", flags)?.SetValue(movement, Time.time + 5f);
                }
            }

            // The spent (exhaustion) indicator, unconditional like the style meter below it -
            // it's a real, separate HUD element now (see GameHud.spentText) and this is the only
            // way to prove it renders somewhere sane without actually sliding four times in a row.
            PlayerMovement moveForSpent = player.GetComponent<PlayerMovement>();

            if (moveForSpent != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(PlayerMovement).GetField("exhaustedUntil", flags)?.SetValue(moveForSpent, Time.time + 3f);
            }

            // Same idea for the style meter - a fresh spawn's multiplier sits at 1 and the whole
            // panel would be hidden (StyleScore.Active), so there'd be nothing in this screenshot
            // to judge. Unconditional, unlike the slide chain above - the score total in
            // particular is meant to be visible in every mode, not just gun game.
            StyleScore style = player.GetComponent<StyleScore>();

            if (style != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(StyleScore).GetField("multiplier", flags)?.SetValue(style, 5.4f);
                typeof(StyleScore).GetField("comboExpiresAt", flags)?.SetValue(style, Time.unscaledTime + 3f);
                typeof(StyleScore).GetField("score", flags)?.SetValue(style, 1450);

                // The breakdown rows under the rank line, same reflection-forcing idea - a bare
                // multiplier alone never draws any, and the real screenshot that found the rank/
                // breakdown overlap bug ("FULL SILVERBACK x5.4" overlapping "1.30x POINT BLANK")
                // specifically needed rows on screen to reproduce at all. lastBreakdown is a
                // readonly List<T> field - readonly only blocks reassigning the field, not
                // mutating the list it already points to, so this can add straight into it.
                var breakdown = typeof(StyleScore).GetField("lastBreakdown", flags)?.GetValue(style)
                    as System.Collections.Generic.List<StyleScore.BreakdownEntry>;

                if (breakdown != null)
                {
                    // GORILLA WARFARE specifically - the longest of the new labels added this
                    // pass, and the one most likely to actually overflow its box.
                    breakdown.Clear();
                    breakdown.Add(new StyleScore.BreakdownEntry { label = "POINT BLANK", shownMultiplier = 1.3f });
                    breakdown.Add(new StyleScore.BreakdownEntry { label = "GORILLA WARFARE", shownMultiplier = 1.6f });
                }

                typeof(StyleScore).GetField("lastBreakdownAt", flags)?.SetValue(style, Time.unscaledTime);
            }

            // The movement combo, bottom left - see MovementCombo.cs. Unconditional like the
            // style meter above, not gated to gun game like the slide chain: it's meant to be
            // visible any time a chain is live, in any mode.
            MovementCombo combo = player.GetComponent<MovementCombo>();

            if (combo != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                typeof(MovementCombo).GetField("chain", flags)?.SetValue(combo, 3);
                typeof(MovementCombo).GetField("expiresAt", flags)?.SetValue(combo, Time.unscaledTime + 2f);
                typeof(MovementCombo).GetField("lastTech", flags)?.SetValue(combo, "GRENADE JUMP");
            }

            yield return null;
            yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(OutputPath);

            // CaptureScreenshot writes out at the *next* end of frame, not this one.
            yield return new WaitForEndOfFrame();
            yield return null;

            Debug.Log($"[hudshot] -> {OutputPath}");

            // The reworked leaderboard - real room property writes rather than reflection, since
            // MatchState.Phase and the per-player stats all read off actual custom properties.
            // Solo room, so only one row - real per-place colour variation needs a second client
            // to check - but this still catches the thing that actually mattered here: a broken
            // or unclosed <color> tag in UpdateStandings would make the whole results screen
            // render wrong, not just look untinted.
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { MatchState.PhaseKey, (int)MatchPhase.Over } });

            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { RoomManager.KillsKey, 7 },
                { RoomManager.DeathsKey, 2 },
                { RoomManager.StyleScoreKey, 1450 },
            });

            yield return null;
            yield return new WaitForEndOfFrame();

            ScreenCapture.CaptureScreenshot(ResultsOutputPath);
            yield return new WaitForEndOfFrame();
            yield return null;

            Debug.Log($"[hudshot] -> {ResultsOutputPath}");

            // The settings screen, forced straight to the Crosshair tab - the new preview only
            // means anything there, and Show() is private (tabs are only ever meant to be
            // switched by clicking one), so this goes through reflection same as everything else
            // on this page that needs to reach past a field/method that's private by design.
            if (SettingsMenu.Instance != null)
            {
                SettingsMenu.Instance.Open();

                var flags3 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                System.Type tabType = typeof(SettingsMenu).GetNestedType("Tab", flags3);
                object crosshairTab = tabType != null ? System.Enum.Parse(tabType, "Crosshair") : null;

                if (crosshairTab != null)
                {
                    typeof(SettingsMenu).GetMethod("Show", flags3)
                        ?.Invoke(SettingsMenu.Instance, new object[] { crosshairTab });
                }

                yield return null;
                yield return new WaitForEndOfFrame();

                ScreenCapture.CaptureScreenshot(SettingsOutputPath);
                yield return new WaitForEndOfFrame();
                yield return null;

                Debug.Log($"[hudshot] -> {SettingsOutputPath}");

                SettingsMenu.Instance.Close();
            }

            // The crate shop - through CrateOpeningScreen.Instance.Open(), the exact same call
            // OpenCrateShopButton makes, rather than loading and instantiating a second, separate
            // copy of the prefab directly. That second-copy shortcut was tried first and missed
            // the real bug entirely: RoomManager's own instance is created already-inactive
            // (Awake never fires on a GameObject instantiated inactive), so Instance was silently
            // never set and every real button click logged "RoomManager never instantiated the
            // screen" - a screenshot of a *manually activated* second copy looked completely fine
            // throughout, because manually activating that copy fired its Awake just fine. Only
            // going through Instance.Open(), the way an actual player does, would have caught it.
            if (CrateOpeningScreen.Instance == null)
            {
                Debug.LogWarning("[hudshot] CrateOpeningScreen.Instance is null - RoomManager "
                                 + "never instantiated the screen, same failure a real player hits");
            }
            else
            {
                CrateOpeningScreen.Instance.Open();

                // A few extra frames - a freshly activated canvas this large (three cards, each
                // several nested rects deep) needs more than one frame for Unity's own layout/
                // canvas rebuild to settle before a screenshot can be trusted to show its actual
                // steady state rather than a mid-rebuild frame.
                for (int i = 0; i < 5; i++)
                    yield return null;

                yield return new WaitForEndOfFrame();

                ScreenCapture.CaptureScreenshot(CrateShopOutputPath);
                yield return new WaitForEndOfFrame();
                yield return null;

                Debug.Log($"[hudshot] -> {CrateShopOutputPath}");

                // Second shot: force the reveal state directly rather than spending real tokens
                // and sitting through a real 5.6s spin - CrateOpeningScreen.Reveal is public
                // enough in spirit but private in practice, so this goes through the same
                // reflection route as everything else on this page.
                CrateOpeningScreen openScreen = CrateOpeningScreen.Instance;
                GameObject openingPageField = GetPrivateField<GameObject>(openScreen, "openingPage");
                GameObject selectPageField = GetPrivateField<GameObject>(openScreen, "selectPage");
                GameObject resultPanelField = GetPrivateField<GameObject>(openScreen, "resultPanel");

                if (openingPageField != null && resultPanelField != null)
                {
                    selectPageField?.SetActive(false);
                    openingPageField.SetActive(true);

                    var flags2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    typeof(CrateOpeningScreen).GetMethod("Reveal", flags2)
                        ?.Invoke(openScreen, new object[] { CrateRarity.Apex });

                    yield return null;
                    yield return new WaitForEndOfFrame();

                    ScreenCapture.CaptureScreenshot(CrateRevealOutputPath);
                    yield return new WaitForEndOfFrame();
                    yield return null;

                    Debug.Log($"[hudshot] -> {CrateRevealOutputPath}");
                }

                CrateOpeningScreen.Instance.Close();
            }

            EditorApplication.Exit(0);
        }
    }
}
