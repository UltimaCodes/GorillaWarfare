using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

// Actually plays the game, in Photon's offline mode, and checks what happened.
//
// The other suites all reason about assets and pure functions. None of them can tell you
// whether a player spawns holding the right bananas, whether the arms are still alive a second
// after they were built, or whether dying gets you back on your feet - all of which are things
// that have been broken at some point without a single check noticing.
//
// Offline mode is a real room with a real local player: Instantiate works, custom properties
// merge and fire their callbacks, and PhotonNetwork.Time runs off a stopwatch so the match
// clock ticks. What it can't cover is a second client, which is still the one thing that needs
// two people and two keyboards.
//
// Lives in Editor/ on purpose - it runs in play mode, but it must never end up in a build.
public static class PlayModeProbe
{
    const string Flag = "GorillaWarfare.Probe";

    public static void Run()
    {
        // Entering play mode reloads the domain, which wipes every static field and every
        // delegate subscription - including the one that was supposed to start this. SessionState
        // survives that reload, so the flag is what carries the intent across.
        SessionState.SetBool(Flag, true);

        // An empty scene, not the menu. Launcher connects to Photon in Start, and offline mode
        // refuses to engage once a connection exists - so starting in the menu means racing it.
        // RoomManager builds itself from a RuntimeInitializeOnLoadMethod when it can't find one,
        // which is exactly the path this needs, so nothing is missing.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);
        new GameObject("~PlayModeProbe").AddComponent<ProbeRunner>();
    }
}

/// Records a transform's local pose after every other LateUpdate has run - the pose that gets
/// drawn. WaitForEndOfFrame would be the obvious way, but it never resumes in batch mode.
[DefaultExecutionOrder(32000)]
public class FinalPoseRecorder : MonoBehaviour
{
    public Transform target;
    public Vector3 localPosition;
    public Vector3 parentLocalPosition;

    void LateUpdate()
    {
        if (target == null)
            return;

        localPosition = target.localPosition;
        parentLocalPosition = target.parent != null ? target.parent.localPosition : Vector3.zero;
    }
}

public class ProbeRunner : MonoBehaviour
{
    const float StepTimeout = 20f;

    const string ProbePrefsPrefix = "gw_probe_";
    const string ProbeBindsPrefix = "gw_probe_bind_";
    const string ProbeWalletPrefix = "gw_probe_wallet_";

    readonly StringBuilder log = new StringBuilder();
    int failures;
    float startedAt;

    void Update()
    {
        if (startedAt > 0f && Time.realtimeSinceStartup - startedAt > 180f)
        {
            Debug.LogError("[play] probe wedged, giving up " + log);
            EditorApplication.Exit(1);
        }
    }

    void Check(bool ok, string label, string detail)
    {
        log.AppendLine($"  {(ok ? "ok  " : "FAIL")}  {label,-46} {detail}");
        if (!ok)
            failures++;
    }

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);

        // First, before anything reads or writes a setting. Batch mode shares the Editor's
        // own PlayerPrefs; without this, every run's settings checks - and the ResetAll at
        // their end - overwrote the settings, keybinds and token balance used in Play Mode.
        // Before the mute, too: switching reloads settings, and reloading applies volume.
        GameSettings.UsePrefsNamespace(ProbePrefsPrefix);
        KeyBinds.UsePrefsNamespace(ProbeBindsPrefix);
        PlayerWallet.UsePrefsNamespace(ProbeWalletPrefix);

        // -nographics suppresses rendering but not audio - without this, a probe run (gunfire,
        // deaths, hitmarkers, a full match's worth of sound) plays out loud on whatever speakers
        // are actually attached. Reported directly: "the game just runs in the background with
        // the audio on."
        AudioListener.volume = 0f;

        // A probe that hangs is worse than one that fails - it looks like it's still working.
        startedAt = Time.realtimeSinceStartup;

        yield return RunProbe();

        // Leaves no probe keys behind. Every reset here only ever touches the probe namespace.
        GameSettings.ResetAll();
        PlayerPrefs.DeleteKey(ProbeWalletPrefix + "Tokens");
        PlayerPrefs.Save();

        Debug.Log("[play] probe\n" + log);
        Debug.Log(failures == 0 ? "[play] ===== ALL PASS =====" : $"[play] {failures} FAILURES");

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    IEnumerator RunProbe()
    {
        if (PhotonNetwork.IsConnected)
        {
            PhotonNetwork.Disconnect();
            yield return Until(() => !PhotonNetwork.IsConnected, "drop any live connection");
        }

        PhotonNetwork.OfflineMode = true;
        PhotonNetwork.NickName = "Probe";

        Check(PhotonNetwork.OfflineMode, "offline mode engaged",
              PhotonNetwork.OfflineMode ? "no server needed" : "refused - something was connected");

        // Short enough to actually watch happen.
        ShortenMatchTimings();

        if (!PhotonNetwork.CreateRoom("probe", new Photon.Realtime.RoomOptions { MaxPlayers = 8 }))
        {
            Check(false, "create an offline room", "CreateRoom refused");
            yield break;
        }

        yield return Until(() => PhotonNetwork.InRoom, "join the room");
        Check(PhotonNetwork.InRoom, "an offline room exists", PhotonNetwork.CurrentRoom?.Name);

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { MatchState.ModeKey, (int)MatchMode.Deathmatch } });

        PhotonNetwork.LoadLevel(MapRegistry.Default.sceneName);
        yield return Until(() => MapRegistry.InMap,
                           "load the game scene");

        // ---- spawning ----
        yield return Until(() => LocalPlayer() != null, "spawn a player");

        PlayerController player = LocalPlayer();
        Check(player != null, "the local player spawned", player != null ? player.name : "never appeared");

        if (player == null)
            yield break;

        // A frame for Start to finish and the deferred destroys inside it to actually happen.
        // The arms bug only showed up one frame after spawning, so checking any sooner would
        // have declared it fine.
        yield return null;
        yield return null;

        // Before anything else that takes time. Spawn protection lasts two seconds and this is
        // the only moment in the run that is reliably inside that window - the first version of
        // this check sat after the screenshot pass and was measuring an expired shield.
        yield return CheckSpawnProtection(player);

        CheckWeapons(player);
        CheckHitboxes(player);
        ReportHitboxCoverage(player);
        ReportScales(player);

        // The one thing no amount of measuring settles: what it actually looks like down the
        // barrel. One shot per weapon, because they are wildly different lengths and a framing
        // that suits the pistol can put the sniper straight through the crosshair.
        yield return CaptureEveryWeapon(player);

        // ---- match clock ----
        log.AppendLine($"  ..    phase {MatchState.Phase} | left {MatchState.TimeLeft:F2}"
                       + $" | warmup length {MatchState.WarmupLength:F2}"
                       + $" | photon time {Photon.Pun.PhotonNetwork.Time:F2}"
                       + $" | realtime {Time.realtimeSinceStartup:F2}");

        Check(MatchState.Phase == MatchPhase.Warmup, "a match starts in warmup", MatchState.Phase.ToString());

        float warmupLeft = MatchState.TimeLeft;
        Check(warmupLeft > 0f, "the clock is running", $"{warmupLeft:F1}s left");

        // Reported by players: damage taken in warmup was still missing when the match went
        // live, because nobody respawns at that moment and health only resets on a new body.
        player.DropProtection();
        player.TakeDamage(100f, "probe", false);
        yield return null;
        float woundedTo = player.HealthFraction;
        Check(woundedTo < 0.5f, "warmup damage lands", $"{woundedTo:P0} health");

        yield return Until(() => MatchState.Phase == MatchPhase.Live, "go live");
        Check(MatchState.Phase == MatchPhase.Live, "warmup becomes live on its own", MatchState.Phase.ToString());

        // The phase change arrives as a room property callback, which may land a frame after
        // Phase reads Live.
        yield return Until(() => player.HealthFraction >= 1f, "health reset for the live match");
        Check(player.HealthFraction >= 1f, "the live match starts everyone on full health",
              $"{woundedTo:P0} in warmup -> {player.HealthFraction:P0} live");

        // ---- switching mode has to reissue weapons ----
        yield return CheckModeChangeReissuesLoadouts();

        // ---- joining and leaving ----
        yield return CheckJoinAndLeaveMessages();

        // ---- the HUD is showing what the game thinks is true ----
        yield return CheckHudReadsTheGame(player);

        // ---- the tab scoreboard actually sits inside its own backdrop ----
        yield return CheckScoreboardLayout();

        // ---- the PSX filter actually changes the picture ----
        yield return CheckPsxFilterVisiblyChangesTheImage();

        // ---- unrelated settings don't rebuild post-processing ----
        yield return CheckUnrelatedSettingsDontRebuildShaders();

        // ---- the grass grows, draws, and knows where you're standing ----
        yield return CheckGrass();

        // ---- the view comes back up after a slide ----
        yield return CheckCameraRecoversFromSlide();

        // ---- the vine swings off a branch rather than pulling you to it ----
        yield return CheckVineSwings();

        // ---- joining mid match ----
        yield return CheckLateJoinGetsWeapons(player);

        // ---- a loadout that resolves to nothing still arms you ----
        yield return CheckEmptyLoadoutFallsBack(player);

        // ---- the settings screen must not leak into the game ----
        yield return CheckSettingsScreenBlocksTheGame(player);

        // ---- settings, keys and the shader stack ----
        yield return CheckSettingsApply();

        // ---- hitstop ----
        yield return CheckHitstopRestores();

        // ---- firing ----
        yield return CheckFiringLeavesAStreak(player);

        // ---- aiming down the banana ----
        yield return CheckAimingDownSights(player);

        // ---- Purple Haze and Red Hot Chili Pepper ----
        yield return CheckPurpleHazeFires(player);
        yield return CheckPurpleHazeRevsOnAim(player);
        yield return CheckRedHotChiliPepper(player);
        yield return CheckChiliChars(player);

        // ---- what an enemy looks like ----
        // Two weapons, because the pose is different: a pistol is one fist, everything longer
        // wants a second hand on it.
        yield return CheckEnemyIsVisible(player, "Rifle");
        yield return CheckEnemyIsVisible(player, "Pistol");

        // The food kit's two, turned into the hand rather than modelled for it - the one view
        // that shows from outside which way round they're held.
        yield return CheckEnemyIsVisible(player, "Gatling");
        yield return CheckEnemyIsVisible(player, "Flamer");

        // ---- dying ----
        yield return CheckDeathAndRespawn();

        // ---- the match has something to say about how it went ----
        List<MatchState.Award> awards = awardsAfterDeath ?? new List<MatchState.Award>();

        // A kill has definitely happened by now - the death check made one - so at least the top
        // scorer award has to have a name on it. An empty list here means the stats never
        // reached the properties the awards are computed from.
        Check(awards.Count > 0, "the match hands out awards",
              awards.Count > 0 ? $"{awards.Count}: {awards[0].title} - {awards[0].who}" : "none");

        // ---- gun game hands out one weapon ----
        yield return CheckGunGameLoadout();

        // ---- a second map ----
        yield return CheckTheZoo();
    }

    /// <summary>
    /// The map system, end to end: pick the zoo the way the host's map picker does, load it the
    /// way the lobby's Start does, and come out standing on one of its spawnpoints with the shared
    /// HUD - exactly one of it - bound to you. Last, because it leaves the arena behind.
    /// </summary>
    IEnumerator CheckTheZoo()
    {
        MapRegistry.Map zoo = MapRegistry.Find("zoo");

        if (zoo.key != "zoo")
        {
            Check(false, "the zoo is a registered map", "not in Resources/Maps.asset");
            yield break;
        }

        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { MapRegistry.RoomKey, zoo.key } });
        yield return Until(() => MapRegistry.Current.key == zoo.key, "pick the zoo");
        Check(MapRegistry.Current.key == zoo.key, "the room remembers the map it picked", MapRegistry.Current.displayName);

        PhotonNetwork.LoadLevel(MapRegistry.Current.sceneName);
        yield return Until(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == zoo.sceneName,
                           "load the zoo");
        yield return Until(() => LocalPlayer() != null, "spawn in the zoo");

        PlayerController player = LocalPlayer();
        Check(player != null, "you spawn in the zoo", player != null ? "spawned" : "never appeared");

        if (player == null)
            yield break;

        yield return null;
        yield return null;

        int huds = Object.FindObjectsByType<GameHud>(FindObjectsSortMode.None).Length;
        Check(huds == 1, "the zoo gets exactly one HUD - the shared one", $"{huds} HUDs");
        Check(player.Hud != null && player.Hud == GameHud.Instance, "the HUD is bound to you",
              player.Hud != null ? "bound" : "not bound");

        float nearest = float.MaxValue;
        foreach (Spawnpoint point in Object.FindObjectsByType<Spawnpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            nearest = Mathf.Min(nearest, Vector3.Distance(point.transform.position, player.transform.position));

        Check(nearest < 1.5f, "you start on one of the zoo's spawnpoints", $"{nearest:F2}m from the nearest");

        PlayerMovement movement = player.GetComponent<PlayerMovement>();
        yield return Until(() => movement != null && movement.Grounded, "land in the zoo");
        Check(movement != null && movement.Grounded && player.transform.position.y > -1f,
              "you're standing on the zoo's floor", $"y {player.transform.position.y:F2}");
    }

    // Everything about a match is measured in minutes, which is correct for playing it and
    // useless for checking it.
    void ShortenMatchTimings()
    {
        MatchState state = MatchState.Instance;
        if (state == null)
        {
            Check(false, "MatchState exists", "RoomManager never built one");
            return;
        }

        Set(state, "warmupSeconds", 2f);
        Set(state, "deathmatchSeconds", 6f);
        Set(state, "gunGameSeconds", 6f);
        Set(state, "scoreboardSeconds", 2f);
        Set(state, "respawnSeconds", 1.5f);
    }

    static void Set(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        info?.SetValue(target, value);
    }

    static T Get<T>(object target, string field) where T : class
    {
        FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        return info?.GetValue(target) as T;
    }

    static PlayerController LocalPlayer()
    {
        foreach (PlayerController controller in Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            if (controller.View != null && controller.View.IsMine)
                return controller;
        }

        return null;
    }

    static Transform Holder(PlayerController player)
    {
        foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ItemHolder")
                return t;
        }

        return null;
    }

    // Deathmatch rolls three weapons for the match and everyone should be carrying that set,
    // not the four weapon fallback.
    void CheckWeapons(PlayerController player)
    {
        Transform holder = Holder(player);
        if (holder == null)
        {
            Check(false, "the player has an ItemHolder", "missing");
            return;
        }

        List<string> built = new List<string>();
        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
            built.Add(gun.name);

        string[] expected = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);

        built.Sort();
        List<string> want = new List<string>(expected);
        want.Sort();

        Check(built.Count == expected.Length, "the loadout is the size the match rolled",
              $"{built.Count} built, {expected.Length} rolled");

        // One weapon, both modes. Deathmatch used to hand out three and let you switch, which
        // made gun game's single weapon feel like a bug rather than the rule.
        Check(built.Count == 1, "you carry exactly one weapon", $"{built.Count}");

        Check(string.Join(",", built) == string.Join(",", want), "the loadout is what the match rolled",
              $"built [{string.Join(",", built)}] against [{string.Join(",", want)}]");

        // Exactly one drawn - the rest are stowed until you switch.
        int active = 0;
        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
        {
            if (gun.gameObject.activeInHierarchy)
                active++;
        }

        Check(active == 1, "exactly one weapon is drawn", $"{active} active");
    }


    // Diagnostics rather than assertions - these are the numbers that decide whether a weapon
    // on somebody else's hand is the right size, and whether the hitboxes are a shape you can
    // aim at or a bubble you bump into.
    void ReportScales(PlayerController player)
    {
        log.AppendLine($"  ..    root lossyScale                               {player.transform.lossyScale}");

        MonkeyRig rig = player.GetComponent<MonkeyRig>();
        if (rig != null && rig.RightHand != null)
        {
            log.AppendLine($"  ..    RightHand lossyScale                          {rig.RightHand.lossyScale}");
            log.AppendLine($"  ..    RightHand world pos                           {rig.RightHand.position}");
        }
        else
        {
            log.AppendLine("  ..    RightHand                                     missing");
        }

        foreach (SkinnedMeshRenderer skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            log.AppendLine($"  ..    skin '{skin.name}' bounds size                {skin.bounds.size}  enabled={skin.enabled} shadows={skin.shadowCastingMode}");
        }

        Transform holder = Holder(player);
        if (holder != null)
            log.AppendLine($"  ..    ItemHolder lossyScale                         {holder.lossyScale}");

        // Where things actually land on screen. Viewport is 0..1 with (0,0) bottom left, so
        // anything outside that range is off frame and anything with z below the near clip is
        // behind the glass.
        Camera cam = PlayerController.LocalCamera;
        if (cam != null && holder != null)
        {
            foreach (Transform child in holder)
            {
                Renderer r = child.GetComponentInChildren<Renderer>(true);
                if (r == null || !child.gameObject.activeInHierarchy)
                    continue;

                Bounds b = r.bounds;
                Vector3 centre = cam.WorldToViewportPoint(b.center);
                Vector3 near = cam.WorldToViewportPoint(b.center - cam.transform.forward * b.extents.magnitude);

                log.AppendLine($"  ..    '{child.name}' viewport centre {centre.x:F2},{centre.y:F2} depth {centre.z:F2}  size {b.size}  nearest depth {near.z:F2}");
            }
        }

        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
        {
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                log.AppendLine($"  ..    weapon '{gun.name}' renderer bounds          {r.bounds.size}  lossy={r.transform.lossyScale}");
                break;
            }
        }

        float biggest = 0f;
        foreach (Hitbox box in player.GetComponentsInChildren<Hitbox>(true))
        {
            SphereCollider col = box.GetComponent<SphereCollider>();
            if (col == null)
                continue;

            Vector3 s = col.transform.lossyScale;
            float worldRadius = col.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            biggest = Mathf.Max(biggest, worldRadius);
        }

        log.AppendLine($"  ..    biggest hitbox world radius                   {biggest:F3}m");

        // A hitbox is meant to be a body part. Anything approaching a metre means it has
        // inherited a scale from the bone it hangs off, which turns it into a wall.
        Check(biggest > 0.01f && biggest < 0.5f, "hitboxes are body sized", $"largest {biggest:F3}m");

        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null)
        {
            log.AppendLine($"  ..    capsule radius/height (world)                 {cc.radius * player.transform.lossyScale.x:F2} / {cc.height * player.transform.lossyScale.y:F2}");
        }
    }

    void CheckHitboxes(PlayerController player)
    {
        Hitbox[] boxes = player.GetComponentsInChildren<Hitbox>(true);
        Check(boxes.Length > 0, "the player can be shot", $"{boxes.Length} hitboxes");

        bool head = false;
        foreach (Hitbox box in boxes)
        {
            if (box.IsHead)
                head = true;
        }

        Check(head, "there is a head to aim at", head ? "found" : "no head hitbox");
    }

    // Offline mode has exactly one player, so there is no remote copy to look at. This builds
    // the same rig a remote copy gets - not hidden from its owner - stands it in front of the
    // camera and photographs it, which is the only way to answer "can you see an enemy" without
    // a second machine.
    /// <summary>
    /// Changing the mode has to change what you're holding.
    ///
    /// This is the bug that made gun game look completely broken. BeginWarmup runs from
    /// OnJoinedRoom - the moment the room exists, long before anyone picks a mode - so everyone
    /// got a deathmatch loadout. Switching to gun game afterwards changed the label and nothing
    /// else, and you played the whole match with the wrong weapons, which is indistinguishable
    /// from the ladder not working.
    /// </summary>
    IEnumerator CheckModeChangeReissuesLoadouts()
    {
        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { MatchState.ModeKey, (int)MatchMode.GunGame } });

        yield return Until(() => MatchState.Mode == MatchMode.GunGame, "switch to gun game");

        // Offline mode applies properties locally and fires the callback, so the reissue path
        // runs exactly as it would online.
        yield return Until(() =>
        {
            string[] carrying = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);
            return carrying.Length == 1 && carrying[0] == WeaponLoadout.GunGameLadder[0];
        }, "be handed the bottom of the ladder");

        string[] now = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);
        Check(now.Length == 1 && now[0] == WeaponLoadout.GunGameLadder[0],
              "gun game hands you rung one", string.Join(",", now));

        Check(MatchState.LadderRung(PhotonNetwork.LocalPlayer) == 0,
              "and starts you at the bottom", $"rung {MatchState.LadderRung(PhotonNetwork.LocalPlayer)}");

        PhotonNetwork.CurrentRoom.SetCustomProperties(
            new Hashtable { { MatchState.ModeKey, (int)MatchMode.Deathmatch } });

        yield return Until(() => MatchState.Mode == MatchMode.Deathmatch, "switch back");
        yield return null;

        string[] back = PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer);
        Check(back.Length == 1, "deathmatch also hands you one weapon", string.Join(",", back));
    }

    /// <summary>
    /// Somebody arriving or leaving has to say so.
    ///
    /// Photon fires these callbacks and nothing was listening, so people vanished mid-fight
    /// with no explanation - which reads as the game being broken rather than as someone
    /// closing it. Offline mode only ever has one player, so the callbacks never fire on their
    /// own here and they're driven directly instead.
    /// </summary>
    IEnumerator CheckJoinAndLeaveMessages()
    {
        MatchState state = MatchState.Instance;
        if (state == null)
        {
            Check(false, "MatchState is listening", "no instance");
            yield break;
        }

        int before = MatchState.Feed.Count;

        state.OnPlayerEnteredRoom(PhotonNetwork.LocalPlayer);
        yield return null;

        bool joined = MatchState.Feed.Count > before
                      && MatchState.Feed[MatchState.Feed.Count - 1].kind == MatchState.FeedKind.Join;

        Check(joined, "arriving posts a message",
              joined ? MatchState.Feed[MatchState.Feed.Count - 1].actor : "nothing was posted");

        before = MatchState.Feed.Count;

        state.OnPlayerLeftRoom(PhotonNetwork.LocalPlayer);
        yield return null;

        bool left = MatchState.Feed.Count > before
                    && MatchState.Feed[MatchState.Feed.Count - 1].kind == MatchState.FeedKind.Leave;

        Check(left, "leaving posts a message",
              left ? MatchState.Feed[MatchState.Feed.Count - 1].actor : "nothing was posted");
    }

    /// <summary>
    /// The freeze has to end.
    ///
    /// Hitstop drags Time.timeScale to near zero, so anything in it that measures itself with
    /// scaled time never gets far enough to let go - and the failure mode isn't a missing
    /// effect, it's the entire game stuck in slow motion with no way out. Worth a check.
    /// </summary>
    IEnumerator CheckHitstopRestores()
    {
        float before = Time.timeScale;

        Juice.Hit(1f);
        yield return null;

        float during = Time.timeScale;
        Check(during < 0.5f, "a kill stops the world", $"timeScale {during:F2}");

        // Real seconds, because scaled ones are barely passing right now - which is exactly the
        // trap this is checking for.
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Time.timeScale < 0.99f && Time.realtimeSinceStartup < deadline)
            yield return null;

        Check(Mathf.Approximately(Time.timeScale, 1f), "and then lets go again",
              $"timeScale back to {Time.timeScale:F2}");

        Check(Mathf.Approximately(Time.fixedDeltaTime, 0.02f), "physics returns to normal",
              $"fixedDeltaTime {Time.fixedDeltaTime:F4}");

        // Shake must not permanently displace the camera either.
        Camera cam = PlayerController.LocalCamera;
        if (cam != null)
        {
            Juice.Shake(1f);
            yield return null;

            deadline = Time.realtimeSinceStartup + 3f;
            Vector3 resting = cam.transform.localPosition;

            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if ((cam.transform.localPosition - resting).sqrMagnitude < 1e-8f)
                    break;
                resting = cam.transform.localPosition;
            }

            Check(true, "the shake settles", $"camera at {cam.transform.localPosition}");
        }
    }

    // A shot has to leave something behind, hit or miss.
    IEnumerator CheckFiringLeavesAStreak(PlayerController player)
    {
        PlayerController.PublishLoadout(new[] { "Rifle" });
        yield return null;
        yield return null;

        Camera cam = PlayerController.LocalCamera;
        SingleShotGun gun = player.ActiveGun;

        if (cam == null || gun == null)
        {
            Check(false, "there is a weapon to fire", "none");
            yield break;
        }

        // Point at something, so the shot has a wall to land on.
        for (int i = 0; i < 12; i++)
        {
            Vector3 direction = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
            if (Physics.Raycast(cam.transform.position, direction, 30f, Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            {
                cam.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                break;
            }
        }

        yield return null;

        gun.Use();

        // Tracers live a twentieth of a second, so this has to look immediately.
        int tracers = Object.FindObjectsByType<BulletTracer>(FindObjectsSortMode.None).Length;
        Check(tracers > 0, "firing leaves a tracer", $"{tracers} in the air");

        Capture(null, "firing");

        yield return null;
    }

    /// <summary>
    /// A direction from the eye with nothing in the way for `distance` metres, flat - the probe
    /// spawns wherever the match puts it, and a fixed direction points into a tree half the time.
    /// </summary>
    static bool ClearLine(Camera cam, float distance, out Vector3 direction)
    {
        for (int i = 0; i < 24; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, i * 15f, 0f) * cam.transform.forward;
            candidate.y = 0f;
            candidate.Normalize();

            if (!Physics.SphereCast(cam.transform.position, 0.4f, candidate, out _, distance + 1.5f,
                                    Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            {
                direction = candidate;
                return true;
            }
        }

        direction = cam.transform.forward;
        return false;
    }

    /// A dummy standing on whatever is under that point, facing the camera - dropped the way the
    /// sandbox drops its own.
    static TrainingDummy DummyAt(Camera cam, Vector3 point)
    {
        Vector3 at = point;
        if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out RaycastHit ground, 20f,
                            Hitbox.WorldMask, QueryTriggerInteraction.Ignore))
            at = ground.point + Vector3.up;

        Vector3 facing = cam.transform.position - at;
        facing.y = 0f;
        return TrainingDummy.Build(at, Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward),
                                   Color.white);
    }

    // The chest of a dummy - its origin is a metre off the ground, about the hips.
    static Vector3 ChestOf(TrainingDummy dummy) => dummy.transform.position + Vector3.up * 0.35f;

    /// <summary>
    /// Points the player at something the way the mouse does - body yaw and look pitch, which
    /// Look() puts back every frame - rather than turning the camera on its own. The weapon
    /// follows the body and the pitch, not the camera, so turning just the camera leaves it
    /// pointing somewhere else: shots still go where the camera looks, but a render shows a gun
    /// slanted across the screen in a way nobody playing ever sees.
    /// </summary>
    static void Face(PlayerController player, Camera cam, Vector3 target)
    {
        cam.transform.localRotation = Quaternion.identity;

        Vector3 to = target - cam.transform.position;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.sqrMagnitude < 0.0001f)
            return;

        float yaw = Quaternion.LookRotation(flat).eulerAngles.y;
        float pitch = -Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;

        Set(player, "horizontalLookRotation", yaw);
        Set(player, "verticalLookRotation", pitch);

        // Now as well as on the next Look, so a shot this frame already goes the right way.
        player.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
        GameObject holder = Get<GameObject>(player, "cameraHolder");
        if (holder != null)
            holder.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }

    /// <summary>
    /// Purple Haze, driven the way PlayerController drives it - the triggers reported through Hold
    /// every frame, the shot through UseHeld - at a dummy 15m out. Grapes have to wait for the
    /// barrel, have to be real things in the air rather than a trace, have to hurt what they
    /// reach, and while it's spun you walk at its slower speed.
    /// </summary>
    IEnumerator CheckPurpleHazeFires(PlayerController player)
    {
        yield return LiveWithTimeLeft(4.5f);

        PlayerController.PublishLoadout(new[] { "Gatling" });
        yield return null;
        yield return null;

        Camera cam = PlayerController.LocalCamera;
        SingleShotGun gun = player.ActiveGun;
        PlayerMovement movement = player.GetComponent<PlayerMovement>();

        if (cam == null || gun == null || gun.name != "Gatling" || movement == null)
        {
            Check(false, "Purple Haze is in hand", gun != null ? gun.name : "nothing");
            yield break;
        }

        if (!ClearLine(cam, 15f, out Vector3 direction))
        {
            Check(false, "room for a dummy 15m out", "every direction is blocked");
            yield break;
        }

        TrainingDummy dummy = DummyAt(cam, cam.transform.position + direction * 15f);
        if (dummy == null)
        {
            Check(false, "a dummy for Purple Haze", "TrainingDummy.Build refused");
            yield break;
        }

        yield return null;

        float startHealth = dummy.Health;
        int startAmmo = gun.Ammo;
        bool firedEarly = false;
        int mostInTheAir = 0;
        float spunSpeed = 1f;
        bool captured = false;
        bool barrelTurned = false;

        // Scaled time throughout - the spin winds on it, and so does the hit freeze every grape
        // that lands sets off. Timed off the asset's own spin-up, so retuning it doesn't break this.
        float spinUp = gun.Info.spinUp;
        float quiet = spinUp * 0.8f;
        float firing = spinUp + 1f;
        Transform barrel = FindChild(gun.transform, "~spin");
        Quaternion barrelAtRest = barrel != null ? barrel.localRotation : Quaternion.identity;

        float began = Time.time;
        while (Time.time - began < firing)
        {
            Face(player, cam, ChestOf(dummy));
            gun.Hold(true, false);
            gun.UseHeld();

            if (Time.time - began < quiet && gun.Ammo != startAmmo)
                firedEarly = true;

            mostInTheAir = Mathf.Max(mostInTheAir, LightProjectile.Live);

            if (gun.Spin >= 1f)
                spunSpeed = movement.WeaponSpeedMultiplier;

            if (barrel != null && Quaternion.Angle(barrel.localRotation, barrelAtRest) > 20f)
                barrelTurned = true;

            if (!captured && Time.time - began > spinUp + 0.5f)
            {
                captured = true;
                CaptureComposite("gatling-firing");
            }

            yield return null;
        }

        int spent = startAmmo - gun.Ammo;
        float dealt = startHealth - dummy.Health;

        Check(!firedEarly, "Purple Haze waits for its barrel",
              firedEarly ? $"fired inside {quiet:F2}s" : $"nothing for {quiet:F2}s of a {spinUp:F1}s spin-up");
        Check(barrelTurned, "and you can see it winding up", barrel == null ? "no spin node on the model" : "the bunch turns");
        Check(spent > 5, "then it fires", $"{spent} grapes in {firing:F1}s");
        Check(mostInTheAir > 3, "grapes are real things in the air", $"{mostInTheAir} at once at most");
        Check(dealt > 0f, "grapes hurt what they reach", $"{dealt:F0} damage to a dummy 15m out");
        Check(Mathf.Abs(spunSpeed - gun.Info.spinMoveMultiplier) < 0.05f, "spun up, you walk slowly",
              $"x{spunSpeed:F2} move speed");

        Object.Destroy(dummy.gameObject);
        yield return null;
    }

    /// Holding aim alone winds the barrel without spending a grape - TF2's rev, waiting at a corner
    /// already spun - and letting go of everything winds it back down and gives you your legs back.
    IEnumerator CheckPurpleHazeRevsOnAim(PlayerController player)
    {
        yield return LiveWithTimeLeft(3.5f);

        // A fresh one - the barrel starts at rest.
        PlayerController.PublishLoadout(new[] { "Gatling" });
        yield return null;
        yield return null;

        SingleShotGun gun = player.ActiveGun;
        PlayerMovement movement = player.GetComponent<PlayerMovement>();

        if (gun == null || gun.name != "Gatling" || movement == null)
        {
            Check(false, "Purple Haze is in hand to rev", gun != null ? gun.name : "nothing");
            yield break;
        }

        int ammo = gun.Ammo;
        float settle = gun.Info.spinUp + 0.2f;
        float began = Time.time;
        while (Time.time - began < settle)
        {
            gun.Hold(false, true);
            yield return null;
        }

        Check(gun.Spin >= 1f && gun.Ammo == ammo, "holding aim spins Purple Haze without firing",
              $"spin {gun.Spin:F2}, {ammo - gun.Ammo} grapes spent");
        Check(gun.Revving, "and everyone else is told it's revving", "Revving rides the player's stream");

        began = Time.time;
        while (Time.time - began < settle)
            yield return null;

        Check(gun.Spin <= 0f && Mathf.Approximately(movement.WeaponSpeedMultiplier, 1f),
              "letting go winds it down", $"spin {gun.Spin:F2}, x{movement.WeaponSpeedMultiplier:F2} move speed");
    }

    /// <summary>
    /// Red Hot Chili Pepper: a second of flame at two dummies on one line, 4m and 10m out. The
    /// near one burns and the far one is out of reach; after the stream stops, the near one keeps
    /// burning (afterburn). Then the airblast spends its fuel once, and not again inside its
    /// cooldown.
    /// </summary>
    IEnumerator CheckRedHotChiliPepper(PlayerController player)
    {
        yield return LiveWithTimeLeft(4.5f);

        PlayerController.PublishLoadout(new[] { "Flamer" });
        yield return null;
        yield return null;

        Camera cam = PlayerController.LocalCamera;
        SingleShotGun gun = player.ActiveGun;

        if (cam == null || gun == null || gun.name != "Flamer")
        {
            Check(false, "Red Hot Chili Pepper is in hand", gun != null ? gun.name : "nothing");
            yield break;
        }

        if (!ClearLine(cam, 10f, out Vector3 direction))
        {
            Check(false, "room for dummies 10m out", "every direction is blocked");
            yield break;
        }

        TrainingDummy near = DummyAt(cam, cam.transform.position + direction * 4f);
        TrainingDummy far = DummyAt(cam, cam.transform.position + direction * 10f);
        if (near == null || far == null)
        {
            Check(false, "dummies for Red Hot Chili Pepper", "TrainingDummy.Build refused");
            yield break;
        }

        yield return null;

        float nearStart = near.Health;
        float farStart = far.Health;
        bool flamingSeen = false;
        bool captured = false;

        float began = Time.time;
        while (Time.time - began < 1f)
        {
            Face(player, cam, ChestOf(near));
            gun.Hold(true, false);
            gun.UseHeld();
            flamingSeen |= gun.Flaming;

            if (!captured && Time.time - began > 0.6f)
            {
                captured = true;
                CaptureComposite("flamer-stream");
                CheckStreamLeavesTheNozzle(cam, gun);

                // Volume rather than isPlaying - the probe's audio is muted and a batch run may
                // have no device, but the source still eases up when the stream is on.
                AudioSource roar = gun.GetComponent<AudioSource>();
                Check(roar != null && roar.volume > 0.05f, "the stream roars",
                      roar == null ? "no sound on the weapon" : $"{roar.clip.name} at {roar.volume:F2}");
            }

            yield return null;
        }

        // A frame for the last puffs to stop counting as the stream.
        yield return null;
        float nearAfterStream = near.Health;

        Check(flamingSeen, "holding fire lights the stream", "Flaming went true");
        Check(nearAfterStream < nearStart, "the flame burns a dummy 4m out", $"{nearStart - nearAfterStream:F0} damage in 1s");
        Check(Mathf.Approximately(far.Health, farStart), "and can't reach one 10m out", $"{farStart - far.Health:F0} damage");

        // Charred where the flame touched it, and only there: some marks, on the near dummy,
        // none on the far one.
        BodyChar sootNear = near.GetComponent<BodyChar>();
        BodyChar sootFar = far.GetComponent<BodyChar>();
        Check(sootNear != null && sootNear.Showing > 0, "the flame chars the body where it hits",
              sootNear == null ? "no char on the dummy" : $"{sootNear.Showing} of {BodyChar.Points} marks");
        Check(sootFar == null || sootFar.Showing == 0, "and not a body it never reached",
              sootFar == null ? "clean" : $"{sootFar.Showing} marks");

        Afterburn burn = near.GetComponent<Afterburn>();
        int flames = burn != null && burn.Fire != null ? burn.Fire.particleCount : 0;
        Check(flames > 10 && burn.Glow != null && burn.Glow.enabled, "a burning body is plainly on fire",
              burn == null ? "never lit" : $"{flames} flames on it, light {(burn.Glow != null && burn.Glow.enabled ? "on" : "off")}");

        // Close enough to see the char and the fire on it.
        Vector3 standBack = near.transform.position - direction * 2.2f + Vector3.up * 0.9f;
        Camera close = new GameObject("~close").AddComponent<Camera>();
        close.enabled = false;
        close.fieldOfView = 50f;
        close.nearClipPlane = 0.05f;
        close.transform.SetPositionAndRotation(standBack, Quaternion.LookRotation(ChestOf(near) - standBack));
        SavePixels(ReadPixels(960, 540, close), "flamer-char-body.png", 960, 540);
        Object.DestroyImmediate(close.gameObject);

        // Puffs already in the air still land for half a second, so the afterburn is measured
        // after they're gone.
        began = Time.time;
        while (Time.time - began < 0.6f)
            yield return null;

        float afterPuffs = near.Health;

        began = Time.time;
        while (Time.time - began < 2f)
            yield return null;

        Check(burn != null && near.Health < afterPuffs, "afterburn keeps burning after the flame stops",
              burn == null ? "never lit" : $"{afterPuffs - near.Health:F0} more over 2s");

        Object.Destroy(near.gameObject);
        Object.Destroy(far.gameObject);
        yield return null;
    }

    /// <summary>
    /// Red Hot Chili Pepper's other half. Played along the floor it chars the ground and the grass
    /// wherever it touches; the airblast throws a dummy - it used to do nothing to one - and kicks
    /// you back ("the airblast should give you some knockback"), spends its fuel once and not
    /// again inside its cooldown.
    /// </summary>
    IEnumerator CheckChiliChars(PlayerController player)
    {
        yield return LiveWithTimeLeft(3.5f);

        PlayerController.PublishLoadout(new[] { "Flamer" });
        yield return null;
        yield return null;

        Camera cam = PlayerController.LocalCamera;
        SingleShotGun gun = player.ActiveGun;
        PlayerMovement movement = player.GetComponent<PlayerMovement>();

        if (cam == null || gun == null || gun.name != "Flamer" || movement == null)
        {
            Check(false, "Red Hot Chili Pepper is in hand again", gun != null ? gun.name : "nothing");
            yield break;
        }

        if (!ClearLine(cam, 6f, out Vector3 direction))
        {
            Check(false, "room to play the flame on the floor", "every direction is blocked");
            yield break;
        }

        // The floor two and a half metres out.
        Vector3 floor = cam.transform.position + direction * 2.5f;
        if (Physics.Raycast(floor + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 10f, Hitbox.WorldMask,
                            QueryTriggerInteraction.Ignore))
            floor = ground.point;

        int charsBefore = BulletDecal.CharCount;
        int grassBefore = GrassChar.Live;

        float began = Time.time;
        while (Time.time - began < 0.7f)
        {
            Face(player, cam, floor);
            gun.Hold(true, false);
            gun.UseHeld();
            yield return null;
        }

        yield return null;
        CaptureComposite("flamer-char-ground");

        Check(BulletDecal.CharCount > charsBefore, "the flame chars the ground it touches",
              $"{BulletDecal.CharCount} char marks, {charsBefore} before");
        Check(GrassChar.Live > grassBefore, "and burns the grass there",
              $"{GrassChar.Live} burnt patches, {grassBefore} before");

        // Once the flames have gone and the char is still at full strength - the moment the
        // stream stops, the fire and its light are on top of whatever's under them.
        began = Time.time;
        while (Time.time - began < 0.6f)
            yield return null;

        Vector4[] sent = Shader.GetGlobalVectorArray("_GrassCharPoints");
        float nearestSent = float.MaxValue;
        if (sent != null)
        {
            for (int i = 0; i < (int)Shader.GetGlobalFloat("_GrassCharCount"); i++)
                nearestSent = Mathf.Min(nearestSent, Vector3.Distance(sent[i], floor));
        }

        log.AppendLine($"  ..    grass char sent: {Shader.GetGlobalFloat("_GrassCharCount"):F0} points, nearest "
                       + $"{nearestSent:F2}m from where the flame was aimed, radius {(sent != null && sent.Length > 0 ? sent[0].w : 0f):F2}");
        CaptureComposite("flamer-char-ground-after");

        // And close up on the burnt patch itself, from a little above and to one side.
        if (sent != null && Shader.GetGlobalFloat("_GrassCharCount") > 0.5f)
        {
            Vector3 patch = sent[0];
            Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
            Vector3 eye = patch - direction * 2.2f + side * 0.8f + Vector3.up * 1.6f;
            Camera close = new GameObject("~closeGround").AddComponent<Camera>();
            close.enabled = false;
            close.fieldOfView = 55f;
            close.nearClipPlane = 0.05f;
            close.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(patch - eye));
            SavePixels(ReadPixels(960, 540, close), "flamer-char-grass.png", 960, 540);
            Object.DestroyImmediate(close.gameObject);
        }

        // Then the airblast, at a dummy 3m out.
        TrainingDummy target = DummyAt(cam, cam.transform.position + direction * 3f);
        if (target == null)
        {
            Check(false, "a dummy to airblast", "TrainingDummy.Build refused");
            yield break;
        }

        yield return null;
        Face(player, cam, ChestOf(target));
        yield return null;

        Vector3 dummyFrom = target.transform.position;
        Vector3 before = movement.Velocity;
        Vector3 back = -cam.transform.forward;

        int fuel = gun.Ammo;
        gun.Airblast();
        int afterOne = gun.Ammo;
        Vector3 kick = movement.Velocity - before;
        gun.Airblast();

        Check(fuel - afterOne == gun.Info.airblastCost && gun.Ammo == afterOne,
              "airblast costs its fuel, once per cooldown", $"{fuel} -> {afterOne} -> {gun.Ammo}");
        Check(Vector3.Dot(kick, back) > gun.Info.airblastSelfKnockback * 0.8f, "and kicks you back",
              $"{Vector3.Dot(kick, back):F1} m/s back into you");

        began = Time.time;
        while (Time.time - began < 0.5f)
            yield return null;

        float thrown = Vector3.Distance(dummyFrom, target.transform.position);
        Check(thrown > 1f, "airblast throws a dummy", $"moved {thrown:F1}m in 0.5s");

        Object.Destroy(target.gameObject);
        movement.ResetVelocity();

        // What it looks like when it's you on fire - your own body isn't drawn, so the flames come
        // up from the bottom of your view instead. Lit harmlessly: no damage a second.
        Afterburn self = Afterburn.On(player.gameObject);
        self.Ignite(1f, 0f, null);

        began = Time.time;
        while (Time.time - began < 0.5f)
            yield return null;

        int licks = self.Fire != null ? self.Fire.particleCount : 0;
        Check(licks > 3, "you can tell you're on fire", $"{licks} flames in your view");
        CaptureComposite("burning-self");
        yield return null;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }

    /// <summary>
    /// The newest puff of flame is on screen where the chili's tip is drawn. The weapon is drawn
    /// by a second, narrower camera, so fire started from the tip's real position came out of a
    /// point dead centre, in front of your face - the stream passed every other check and looked
    /// like it came from nowhere.
    /// </summary>
    void CheckStreamLeavesTheNozzle(Camera cam, SingleShotGun gun)
    {
        ViewModelCamera viewModel = cam.GetComponent<ViewModelCamera>();
        Camera weaponCam = viewModel != null ? viewModel.WeaponCamera : null;

        LightProjectile newest = null;
        foreach (LightProjectile puff in Object.FindObjectsByType<LightProjectile>(FindObjectsSortMode.None))
        {
            if (newest == null || puff.transform.localScale.x < newest.transform.localScale.x)
                newest = puff;
        }

        if (weaponCam == null || newest == null)
        {
            Check(false, "the stream leaves the nozzle", weaponCam == null ? "no weapon camera" : "no puffs in the air");
            return;
        }

        Vector2 puffOnScreen = cam.WorldToViewportPoint(newest.transform.position);
        Vector2 tipOnScreen = weaponCam.WorldToViewportPoint(gun.TipPosition);
        float apart = Vector2.Distance(puffOnScreen, tipOnScreen);

        Check(apart < 0.12f, "the stream leaves the nozzle",
              $"newest puff at {puffOnScreen.x:F2},{puffOnScreen.y:F2}, tip drawn at {tipOnScreen.x:F2},{tipOnScreen.y:F2}");
    }

    // Right click on Big Mike. Driven straight through UpdateAim, because batch mode has no
    // mouse and this is the only way to see whether any of it moves.
    IEnumerator CheckAimingDownSights(PlayerController player)
    {
        // A new warmup hands out a fresh random weapon, and the probe's matches last six seconds -
        // so the sniper this check hands itself could be swapped for something that can't aim
        // before the check had finished, depending only on where in the match it landed.
        yield return LiveWithTimeLeft(4f);

        PlayerController.PublishLoadout(new[] { "Sniper" });
        yield return null;
        yield return null;

        Camera cam = PlayerController.LocalCamera;
        Transform holder = Holder(player);

        if (cam == null || holder == null)
        {
            Check(false, "there is a camera and a holder to aim with", "missing");
            yield break;
        }

        float hipFov = cam.fieldOfView;
        Vector3 hipPosition = holder.localPosition;

        GunInfo sniper = Resources.Load<GunInfo>("Guns/Sniper");
        Check(sniper != null && sniper.canAim, "Big Mike aims", sniper == null ? "no asset" : $"canAim={sniper.canAim}");

        // Wait for it to arrive rather than for a number of frames. The transition is an
        // exponential lerp against deltaTime, so how far it gets in ninety frames depends
        // entirely on the frame rate - and batch mode runs at several hundred, which left it
        // stalled a third of the way there and looking like a broken feature.
        PlayerController.AimInputOverride = true;
        yield return Until(() => Mathf.Abs(cam.fieldOfView - sniper.aimFov) < 0.5f, "finish aiming");

        float aimedFov = cam.fieldOfView;
        Vector3 aimedPosition = holder.localPosition;

        log.AppendLine($"  ..    fov {hipFov:F1} -> {aimedFov:F1}, holder x {hipPosition.x:F3} -> {aimedPosition.x:F3}");

        Check(aimedFov < hipFov - 10f, "aiming narrows the view", $"{hipFov:F0} down to {aimedFov:F0}");
        Check(player.IsAiming, "the player reports aiming", "IsAiming true");

        // Counter-Strike style: the weapon isn't repositioned, it's simply not drawn. Posing it
        // could never work - narrowing the field of view magnifies whatever is in it, and the
        // weapon is in it, so it grew exactly as fast as the world did and filled the screen.
        int visible = 0;
        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
        {
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled && gun.gameObject.activeInHierarchy)
                    visible++;
            }
        }

        Check(visible == 0, "the weapon is out of the way", $"{visible} renderers still drawn");

        // The scope overlay is HUD rather than weapon, and it only exists on a weapon that can
        // aim - so it's checked here, holding the sniper, rather than in the HUD check, which
        // runs while you're carrying whatever the match happened to roll.
        GameObject scopeOverlay = GameHud.Instance != null
            ? Get<GameObject>(GameHud.Instance, "scope") : null;

        Check(scopeOverlay != null && scopeOverlay.activeSelf, "the scope comes up over the screen",
              scopeOverlay == null ? "the HUD has no scope"
              : scopeOverlay.activeSelf ? "covering everything but the glass" : "still hidden");

        PlayerController.AimInputOverride = true;
        yield return null;

        // The scoped view itself can't be photographed from here. Camera.Render draws the scene
        // and nothing else, so IMGUI never lands in it, and ScreenCapture needs an end of frame
        // that batch mode never reaches - it hung the whole probe until the watchdog killed it.
        // The scene behind the scope is capturable, and the mask is checkable on its own.
        Capture(null, "aiming");
        CheckScopeMask();

        // And back again, or you'd be stuck scoped for the rest of the match.
        PlayerController.AimInputOverride = false;
        yield return Until(() => Mathf.Abs(cam.fieldOfView - hipFov) < 0.5f, "come back off the scope");

        Check(Mathf.Abs(cam.fieldOfView - hipFov) < 1.5f, "letting go returns the view",
              $"back to {cam.fieldOfView:F0}");
        Check(!player.IsAiming, "the player stops aiming", "IsAiming false");

        Check(scopeOverlay == null || !scopeOverlay.activeSelf, "and the scope drops away",
              "otherwise you finish the match looking down a tube");

        // And it comes back, or you'd have an invisible banana for the rest of the round.
        int redrawn = 0;
        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
        {
            if (!gun.gameObject.activeInHierarchy)
                continue;

            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                if (r.enabled)
                    redrawn++;
            }
        }

        Check(redrawn > 0, "the weapon comes back", $"{redrawn} renderers drawn again");

        PlayerController.AimInputOverride = null;
    }

    IEnumerator CheckEnemyIsVisible(PlayerController player, string weapon)
    {
        Camera camera = PlayerController.LocalCamera;
        if (camera == null)
            yield break;

        // Spawn points are random and several of them face a wall, so a fixed offset forward
        // put the stand-in inside the geometry about half the time. Find a direction with room
        // in it first, then aim at what we placed.
        const float range = 4.5f;
        Vector3 eye = camera.transform.position;
        Vector3 direction = camera.transform.forward;

        for (int i = 0; i < 12; i++)
        {
            Vector3 candidate = Quaternion.Euler(0f, i * 30f, 0f) * camera.transform.forward;
            candidate.y = 0f;
            candidate.Normalize();

            if (!Physics.Raycast(eye, candidate, range + 1.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                direction = candidate;
                break;
            }
        }

        GameObject stand = new GameObject("~enemy stand-in");
        stand.transform.position = eye + direction * range - Vector3.up * 0.9f;
        stand.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);

        camera.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

        MonkeyRig rig = stand.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Check(false, "an enemy body can be built", "MonkeyRig.Build refused");
            Object.DestroyImmediate(stand);
            yield break;
        }

        // Same weapon treatment a remote copy gets.
        Transform hand = rig.RightHand;
        if (hand != null)
        {
            GameObject held = new GameObject(weapon);
            held.transform.SetParent(hand, false);
            Hitbox.Neutralise(held.transform);

            GunInfo info = Resources.Load<GunInfo>("Guns/" + weapon);
            SingleShotGun gun = held.AddComponent<SingleShotGun>();
            gun.Configure(info, null, false);

            // Nothing feeds the rig here - there's no PlayerController on the stand-in - so
            // the grip style is set the way one would.
            rig.TwoHandedGrip = info == null || info.twoHanded;

            log.AppendLine($"  ..    enemy hand lossyScale {hand.lossyScale}  weapon lossyScale {held.transform.lossyScale * hand.lossyScale.x}");

            // The same offset the real thing uses, read off the prefab rather than guessed -
            // otherwise this measures a weapon sitting at the hand's origin, which is not where
            // anybody ever sees one.
            GameObject prefab = Resources.Load<GameObject>("PhotonPrefabs/PlayerController");
            PlayerController template = prefab != null ? prefab.GetComponent<PlayerController>() : null;

            if (template != null)
            {
                object offset = typeof(PlayerController)
                    .GetField("weaponHandOffset", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(template);

                if (offset is Vector3 local)
                    held.transform.localPosition = local;
            }
        }

        yield return null;
        yield return null;

        // Where the weapon ends up on the body, as a fraction of how tall the body is.
        //
        // "The banana sits at hip height" has been on the known-issues list for a while without
        // anybody putting a number on it, and a fraction is the only way to say it that doesn't
        // depend on how big the gorilla happens to be. Hips are around 0.5, the chest 0.7.
        if (hand != null)
        {
            // Both ends from the mesh bounds. The first version of this used the stand-in's
            // transform as the floor, which is an arbitrary point some way up the body - it
            // reported the gorilla as 0.87m tall when every other check in this probe measures
            // it at 1.95m, and every fraction built on it was wrong.
            float feet = float.MaxValue;
            float head = float.MinValue;

            foreach (SkinnedMeshRenderer skin in stand.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                feet = Mathf.Min(feet, skin.bounds.min.y);
                head = Mathf.Max(head, skin.bounds.max.y);
            }

            float height = Mathf.Max(0.01f, head - feet);
            float carried = (hand.position.y - feet) / height;

            Check(carried > 0.5f, $"{weapon}: the weapon is carried above the hips",
                  $"{carried:P0} of body height");

            // Print the skeleton the grips are measured against, so the numbers that fix this
            // come from the rig rather than from taste.
            Transform spine = Get<Transform>(rig, "spine");
            Transform shoulder = Get<Transform>(rig, "rightUpperArm");
            Transform crown = Get<Transform>(rig, "head");

            log.AppendLine($"  ..    body {height:F2}m | spine {(spine != null ? (spine.position.y - feet) / height : 0f):P0}"
                           + $" | shoulder {(shoulder != null ? (shoulder.position.y - feet) / height : 0f):P0}"
                           + $" | head {(crown != null ? (crown.position.y - feet) / height : 0f):P0}"
                           + $" | hand {carried:P0}");
        }

        bool visible = false;
        float tallest = 0f;

        foreach (SkinnedMeshRenderer skin in stand.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            tallest = Mathf.Max(tallest, skin.bounds.size.y);

            if (skin.enabled && skin.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly)
                visible = true;
        }

        Check(visible, "an enemy body actually renders",
              visible ? "not shadows-only" : "every renderer is shadows-only or off");

        Check(tallest > 1.2f && tallest < 3f, "an enemy is person sized", $"{tallest:F2}m tall");

        // Weapon on the hand must be a banana, not a building.
        float weaponSize = 0f;
        foreach (SingleShotGun gun in stand.GetComponentsInChildren<SingleShotGun>(true))
        {
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
                weaponSize = Mathf.Max(weaponSize, r.bounds.size.magnitude);
        }

        Check(weaponSize > 0.05f && weaponSize < 2.5f, "an enemy's weapon is weapon sized",
              $"{weaponSize:F2}m");

        CheckArmsAreGripping(stand, weapon);

        Capture(null, "enemy-" + weapon.ToLower());

        Object.DestroyImmediate(stand);
        yield return null;
    }

    // A straight arm is an arm reaching at something; a bent one is an arm holding something.
    // Measuring how far the hand is from the shoulder against how far it could possibly be is
    // the difference, and it needs no knowledge of the rig's bone axes.
    void CheckArmsAreGripping(GameObject stand, string weapon)
    {
        Transform upper = FindBone(stand.transform, "RIGHTSHOULDER");
        Transform fore = FindBone(stand.transform, "RIGHTELBOW");
        Transform hand = FindBone(stand.transform, "RIGHTHOLD");

        if (upper == null || fore == null || hand == null)
        {
            Check(false, "the right arm chain is present", "a joint is missing");
            return;
        }

        float span = Vector3.Distance(upper.position, fore.position)
                     + Vector3.Distance(fore.position, hand.position);
        float reach = Vector3.Distance(upper.position, hand.position);
        float extension = span > 0.0001f ? reach / span : 1f;

        log.AppendLine($"  ..    right arm extension {extension:P0} ({reach:F2}m of a possible {span:F2}m)");

        // Fully straight is 100%. Anything above about 95% is a zombie reach, which is what the
        // fixed angle pose produced.
        Check(extension < 0.95f, "arms are bent, not reaching", $"{extension:P0} extended");

        // And the hand has to be in front of the chest rather than out to the side or behind.
        Vector3 chestToHand = hand.position - stand.transform.position;
        float forward = Vector3.Dot(chestToHand.normalized, stand.transform.forward);

        Check(forward > 0.2f, $"{weapon}: the gun hand is held in front", $"forward dot {forward:F2}");

        // The off hand is the whole point of the one handed change: on a pistol it should be
        // down by the body, on anything longer it should be up near the weapon.
        // The hand, not the elbow. An elbow sits below the hand in any grip, so measuring it
        // said almost the same thing for both poses and proved nothing.
        Transform offElbow = FindBone(stand.transform, "LEFTELBOW");
        Transform offHand = offElbow != null && offElbow.childCount > 0 ? offElbow.GetChild(0) : offElbow;
        GunInfo info = Resources.Load<GunInfo>("Guns/" + weapon);

        if (offHand != null && info != null)
        {
            float offHandHeight = offHand.position.y - stand.transform.position.y;
            float gunHandHeight = hand.position.y - stand.transform.position.y;

            log.AppendLine($"  ..    {weapon}: off hand at {offHandHeight:F2}m, gun hand at {gunHandHeight:F2}m");

            if (info.twoHanded)
            {
                Check(offHandHeight > gunHandHeight - 0.25f, $"{weapon}: both hands are on it",
                      $"off hand {offHandHeight:F2}m against gun hand {gunHandHeight:F2}m");
            }
            else
            {
                Check(offHandHeight < gunHandHeight - 0.25f, $"{weapon}: the off hand is out of it",
                      $"off hand {offHandHeight:F2}m against gun hand {gunHandHeight:F2}m");
            }
        }
    }

    static Transform FindBone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }

    List<MatchState.Award> awardsAfterDeath;

    IEnumerator CheckDeathAndRespawn()
    {
        PlayerController player = LocalPlayer();
        if (player == null)
        {
            Check(false, "a player to kill", "none");
            yield break;
        }

        int feedBefore = MatchState.Feed.Count;

        // Only a live match scores a death, and the probe's matches last six seconds - so this
        // waits for one with time left rather than dying into a warmup that wipes the stats. The
        // awards check below used to pass or fail on exactly that timing.
        yield return LiveWithTimeLeft(2.5f);

        // Straight through the interface a bullet uses, so this exercises the real path.
        player.TakeDamage(500f, "Pistol", true);

        yield return Until(() => RoomManager.AwaitingRespawn, "register the death");

        // Read now, before the match can roll over and reset them.
        awardsAfterDeath = MatchState.Awards();
        Check(RoomManager.AwaitingRespawn, "dying starts a respawn timer",
              $"{RoomManager.RespawnAt - Time.time:F1}s");

        Check(MatchState.Feed.Count > feedBefore, "the kill feed heard about it",
              $"{MatchState.Feed.Count - feedBefore} new entries");

        if (MatchState.Feed.Count > 0)
        {
            MatchState.FeedEntry last = MatchState.Feed[MatchState.Feed.Count - 1];

            Check(last.kind == MatchState.FeedKind.Kill, "it is recorded as a kill", last.kind.ToString());
            Check(!string.IsNullOrEmpty(last.subject), "the feed names the victim", last.subject);
            Check(last.headshot, "the feed carries the headshot flag", "headshot");
            Check(last.involvesYou, "your own kills are marked", "involvesYou");
        }

        Check(Object.FindFirstObjectByType<Camera>() != null, "something is still rendering",
              "a camera survives the controller");

        Check(LocalPlayer() == null, "the controller is gone while dead", "destroyed");

        // PSX normally runs on the gun camera, and the death camera doesn't have one - so while
        // dead it has to fall back to the world profile, or the look vanishes every respawn.
        yield return null;
        yield return null;

        Check(!GameSettings.PsxFilter || PsxWhere() == "world", "psx stays on while you're dead",
              $"psx {(GameSettings.PsxFilter ? "on" : "off")}, running on: {PsxWhere()}");

        // Dying shouldn't change how the world looks - the death camera draws the same toon
        // outline as the player camera. It's built a moment after the death registers.
        // Found by name rather than through LocalCamera, which a quick respawn hands straight back
        // to the new player camera - that would pass this without ever looking at the death one.
        yield return Until(() => GameObject.Find("~DeathCamera") != null || LocalPlayer() != null, "build the death camera");
        GameObject deathCamera = GameObject.Find("~DeathCamera");
        Check(deathCamera != null && deathCamera.GetComponent<ScreenOutline>() != null,
              "the death camera draws the toon outline",
              deathCamera == null ? "respawned before a death camera was found" : "outlined");

        // Reported by players: "the psx filter when turned on doesnt affect the replay camera".
        // The check above only proves PSX is in the profile; this proves the picture changes.
        Camera deathView = deathCamera != null ? deathCamera.GetComponent<Camera>() : null;

        if (deathView != null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            bool psxBefore = GameSettings.PsxFilter;

            GameSettings.SetPsxFilter(false);
            yield return null;
            yield return null;
            Color32[] off = deathView != null ? ReadPixels(deathView) : null;

            GameSettings.SetPsxFilter(true);
            yield return null;
            yield return null;
            Color32[] on = deathView != null ? ReadPixels(deathView) : null;

            GameSettings.SetPsxFilter(psxBefore);

            if (off != null && on != null)
            {
                SavePixels(off, "psx-deathcam-off.png");
                SavePixels(on, "psx-deathcam-on.png");

                double diff = AverageDifference(off, on);
                Check(diff > 0.5, "the psx filter changes the death camera's picture",
                      $"average per-channel difference {diff:F2}");
            }
            else
            {
                Check(false, "the psx filter changes the death camera's picture", "respawned mid-check");
            }
        }

        yield return Until(() => LocalPlayer() != null, "respawn");

        PlayerController respawned = LocalPlayer();
        Check(respawned != null, "you come back", respawned != null ? "new controller" : "never respawned");
        Check(!RoomManager.AwaitingRespawn, "the respawn timer cleared", "cleared");

        yield return null;
        yield return null;

        Check(!GameSettings.PsxFilter || PsxWhere() == "gun camera", "psx moves back onto the gun after respawning",
              $"running on: {PsxWhere()}");

        if (respawned != null)
        {
            CheckHitboxes(respawned);
        }
    }

    // Gun game gives you exactly one weapon, and changing the loadout property has to rebuild
    // what you are holding without respawning you.
    IEnumerator CheckGunGameLoadout()
    {
        PlayerController player = LocalPlayer();
        if (player == null)
            yield break;

        PlayerController.PublishLoadout(new[] { "Peel" });

        yield return null;
        yield return null;

        List<string> built = new List<string>();
        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
            built.Add(gun.name);

        Check(built.Count == 1 && built[0] == "Peel", "a one weapon loadout rebuilds in place",
              built.Count == 0 ? "nothing built" : string.Join(",", built));

    }

    IEnumerator CaptureEveryWeapon(PlayerController player)
    {
        // One looking down at the floor, to judge the map surfacing rather than the weapon.
        Camera down = PlayerController.LocalCamera;
        if (down != null)
        {
            Quaternion was = down.transform.rotation;
            down.transform.rotation = Quaternion.Euler(28f, was.eulerAngles.y, 0f);
            yield return null;
            Capture(null, "map-floor");
            down.transform.rotation = was;
            yield return null;
        }

        // Peel isn't in AllWeapons - it's the guaranteed melee weapon, never one of the ones a
        // random loadout rolls - but that's exactly why nothing had ever screenshotted it here.
        // Its held pose was being judged from an isolated calibration render instead of a real
        // equipped shot; added explicitly so it gets the same "a person can actually look at this"
        // treatment as everything else in this loop.
        List<string> toCapture = new List<string>(WeaponLoadout.AllWeapons) { "Peel" };

        foreach (string weapon in toCapture)
        {
            PlayerController.PublishLoadout(new[] { weapon });

            // A frame for the property callback, a frame for the rebuild to settle.
            yield return null;
            yield return null;

            ReportViewport(player, weapon);
            CaptureComposite("viewmodel-" + weapon.ToLower());
            CaptureSide(player, "side-" + weapon.ToLower());
        }
    }

    /// <summary>
    /// The weapon in your hands seen from its right, on its own layer against grey - the camera's
    /// forward runs left to right across the picture. Down the barrel, a model turned into the
    /// hand can look right both ways round; from the side it can't.
    /// </summary>
    void CaptureSide(PlayerController player, string name)
    {
        SingleShotGun gun = player.ActiveGun;
        Camera eye = PlayerController.LocalCamera;
        int layer = LayerMask.NameToLayer(ViewModelCamera.LayerName);

        if (gun == null || eye == null || layer < 0
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;

        Bounds bounds = new Bounds(gun.transform.position, Vector3.zero);
        foreach (Renderer r in gun.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled)
                bounds.Encapsulate(r.bounds);
        }

        GameObject host = new GameObject("~side camera");
        Camera side = host.AddComponent<Camera>();
        side.enabled = false;
        side.cullingMask = 1 << layer;
        side.clearFlags = CameraClearFlags.SolidColor;
        side.backgroundColor = new Color(0.35f, 0.35f, 0.38f);
        side.fieldOfView = 30f;
        side.nearClipPlane = 0.01f;

        float reach = Mathf.Max(0.3f, bounds.extents.magnitude) / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.2f;
        host.transform.position = bounds.center + eye.transform.right * reach;
        host.transform.rotation = Quaternion.LookRotation(bounds.center - host.transform.position, eye.transform.up);

        SavePixels(ReadPixels(side), name + ".png");
        Object.DestroyImmediate(host);
    }

    void ReportViewport(PlayerController player, string weapon)
    {
        Camera cam = PlayerController.LocalCamera;
        Transform holder = Holder(player);
        if (cam == null || holder == null)
            return;

        foreach (Transform child in holder)
        {
            Renderer r = child.GetComponentInChildren<Renderer>(true);
            if (r == null || !child.gameObject.activeInHierarchy)
                continue;

            Bounds b = r.bounds;
            Vector3 centre = cam.WorldToViewportPoint(b.center);

            log.AppendLine($"  ..    {weapon,-9} '{child.name,-16}' centre {centre.x:F2},{centre.y:F2} depth {centre.z:F2} nearest {NearestDepth(cam, b):F2} len {b.size.magnitude:F2}");

            // Which way round it's held: the grip end and the muzzle end on screen, with depth. A
            // model turned into the hand (the food kit's) can come out backwards, and one render
            // from behind doesn't say which end is nearer.
            SingleShotGun gun = child.GetComponent<SingleShotGun>();
            if (gun != null)
            {
                Vector3 grip = cam.WorldToViewportPoint(child.position);
                Vector3 tip = cam.WorldToViewportPoint(gun.TipPosition);
                log.AppendLine($"  ..    {weapon,-9} grip {grip.x:F2},{grip.y:F2} depth {grip.z:F2} -> tip {tip.x:F2},{tip.y:F2} depth {tip.z:F2}");
            }
        }
    }

    /// How close the nearest corner of a bounding box gets to the camera.
    ///
    /// This used to push the centre back by the bounds' radius, which treats a long thin banana
    /// held at an angle as a sphere - so it reported the sniper poking through the near clip
    /// when it wasn't, and would have had me shoving the whole viewmodel forward to fix a
    /// problem that didn't exist. Eight corners is barely more work and is the actual answer.
    static float NearestDepth(Camera cam, Bounds b)
    {
        float nearest = float.MaxValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z);

            nearest = Mathf.Min(nearest, cam.WorldToViewportPoint(corner).z);
        }

        return nearest;
    }

    /// <summary>
    /// The HUD is a scene object now, so nothing about it is guaranteed by the compiler.
    ///
    /// A screenshot can't settle this: the HUD is a screen space overlay canvas, and overlay
    /// canvases don't appear in a camera rendered to a texture, which is the only kind of
    /// picture this probe can take. So rather than looking at it, this reads what the labels
    /// actually say and compares them against the state they're supposed to be reporting. That
    /// catches what a screenshot wouldn't anyway - a number that's present, correctly placed
    /// and stale.
    /// </summary>
    IEnumerator CheckHudReadsTheGame(PlayerController player)
    {
        GameHud hud = GameHud.Instance;

        if (hud == null)
        {
            Check(false, "the HUD is in the scene", "no GameHud - run the HUD builder");
            yield break;
        }

        Check(player.Hud == hud, "the player found the HUD",
              player.Hud == hud ? "the one in the scene"
              : player.Hud == null ? "player never bound one" : "bound to something else");

        // A couple of frames for Update to push the game's state into the labels.
        yield return null;
        yield return null;

        TMP_Text health = Get<TMP_Text>(hud, "healthNumber");
        Check(health != null && health.text == player.HealthPoints.ToString(),
              "the health number matches the player",
              health == null ? "no label" : $"says {health.text}, player has {player.HealthPoints}");

        SingleShotGun gun = player.ActiveGun;

        if (gun != null && gun.Info != null && !gun.Info.melee)
        {
            TMP_Text rounds = Get<TMP_Text>(hud, "ammoNumber");
            Check(rounds != null && rounds.text == gun.Ammo.ToString(),
                  "the round count matches the magazine",
                  rounds == null ? "no label" : $"says {rounds.text}, magazine holds {gun.Ammo}");

            // The bare spare count is the whole reason it reads as a count rather than as a
            // multiplier. An x creeping back in here is a regression.
            TMP_Text spare = Get<TMP_Text>(hud, "spareNumber");
            Check(spare != null && !spare.text.Contains("x"), "spare bananas are a bare number",
                  spare == null ? "no label" : spare.text);
        }

        TMP_Text weapon = Get<TMP_Text>(hud, "weaponName");
        string expected = gun != null ? WeaponLoadout.DisplayName(gun.name).ToUpper() : null;
        Check(weapon != null && expected != null && weapon.text == expected,
              "the weapon name matches what you're holding",
              weapon == null ? "no label" : $"says {weapon.text}, holding {expected}");

        // The clock has to be counting something. A HUD that reports 0:00 through a live match
        // is worse than no clock at all, because you'd believe it.
        TMP_Text clock = Get<TMP_Text>(hud, "clock");
        Check(clock != null && clock.text.Contains(":") && clock.text != "0:00",
              "the clock is running", clock == null ? "no label" : clock.text);

        // ---- the feed actually renders a row ----
        RectTransform feed = Get<RectTransform>(hud, "feedContainer");
        MatchState state = MatchState.Instance;

        if (feed != null && state != null)
        {
            state.OnPlayerEnteredRoom(PhotonNetwork.LocalPlayer);
            yield return null;
            yield return null;

            string written = null;

            foreach (TMP_Text row in feed.GetComponentsInChildren<TMP_Text>())
            {
                if (row.gameObject.activeInHierarchy && !string.IsNullOrEmpty(row.text))
                {
                    written = row.text;
                    break;
                }
            }

            // Pooled off a hidden template, so "a row exists" is not the same as "a row is
            // visible with words in it" - the template never waking up is the failure mode.
            Check(written != null, "a feed line reaches the screen", written ?? "no visible row");
        }

        // ---- damage numbers ----
        RectTransform numbers = Get<RectTransform>(hud, "damageContainer");

        if (numbers != null && PlayerController.LocalCamera != null)
        {
            // Three metres in front of the camera, so it projects onto the screen rather than
            // behind it, which the HUD correctly refuses to draw.
            hud.ShowDamage(PlayerController.LocalCamera.transform.position
                           + PlayerController.LocalCamera.transform.forward * 3f, 24f, false);

            yield return null;
            yield return null;

            bool drawn = false;

            foreach (TMP_Text label in numbers.GetComponentsInChildren<TMP_Text>())
                drawn |= label.gameObject.activeInHierarchy && label.text == "24";

            Check(drawn, "a damage number reaches the screen", drawn ? "24" : "nothing visible");
        }
    }

    /// <summary>
    /// Somebody joins holding a loadout that names weapons which no longer exist.
    ///
    /// This is not hypothetical: PUN never clears player custom properties, not even between
    /// rooms, so a loadout written by an older build follows you into a newer one. Every name in
    /// it fails to load, nothing gets built, and you spawn into a live match with empty hands -
    /// which is exactly what Ryaan hit. The names below are deliberately rubbish.
    /// </summary>
    IEnumerator CheckEmptyLoadoutFallsBack(PlayerController player)
    {
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { PlayerController.LoadoutKey, "M1911,AK74" },
        });

        yield return Until(() => PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer).Length == 2,
                           "take a loadout from a build that no longer exists");

        // Two frames: one for the property callback to rebuild, one for the objects to settle.
        yield return null;
        yield return null;

        int armed = 0;

        foreach (SingleShotGun gun in player.GetComponentsInChildren<SingleShotGun>(true))
            armed++;

        Check(armed > 0, "a dead loadout still leaves you armed", $"{armed} weapons built");

        SingleShotGun holding = player.ActiveGun;
        Check(holding != null && holding.name == WeaponLoadout.Fallback,
              "and what you get is the fallback",
              holding == null ? "nothing equipped" : holding.name);

        // Put it back, or every check after this one is measuring the fallback.
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { PlayerController.LoadoutKey, MatchState.Rules.Serialise(
                MatchState.WeaponsFor(PhotonNetwork.LocalPlayer)) },
        });

        yield return null;
        yield return null;
    }

    /// <summary>
    /// The settings actually reach the things they claim to control.
    ///
    /// A settings screen is the easiest kind of thing to build wrong and not notice: every
    /// slider moves, every value saves, and nothing happens. So this changes them through the
    /// same API the UI uses and then checks the game, not the setting.
    /// </summary>
    IEnumerator CheckSettingsApply()
    {
        // ---- the shader stack exists and reaches the camera ----
        Check(ShaderStack.Instance != null, "the shader stack is running",
              ShaderStack.Instance != null ? "on RoomManager" : "RoomManager never built one");

        Camera camera = PlayerController.LocalCamera;

        if (camera != null)
        {
            var layer = camera.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessLayer>();

            // The whole reason post-processing has never rendered: there was a volume and a
            // profile in the scene since 2024 and nothing ever put a layer on the camera.
            Check(layer != null, "the camera has a post process layer",
                  layer != null ? "attached" : "nothing renders post processing without one");
        }

        // ---- presets rebuild the volume ----
        GameSettings.ShaderPreset before = GameSettings.Shaders;

        // Motion blur and the PSX filter are both deliberately independent of the preset ladder
        // (see GameSettings.PsxFilter/MotionBlur) - PsxFilter defaults to true now, so isolating
        // "does the preset alone drive the volume" needs both pinned off first, or Off would
        // legitimately still have a volume for the filter that's still on regardless of preset.
        bool motionBlurBefore = GameSettings.MotionBlur;
        bool psxBefore = GameSettings.PsxFilter;
        GameSettings.SetMotionBlur(false);
        GameSettings.SetPsxFilter(false);

        GameSettings.SetShaders(GameSettings.ShaderPreset.Off);

        // Two frames, because Destroy is deferred to the end of the frame it was called in - a
        // single yield counts a volume that is already on its way out.
        yield return null;
        yield return null;

        string offVolumes = LiveVolumes();

        GameSettings.SetShaders(GameSettings.ShaderPreset.Overripe);
        yield return null;
        yield return null;

        string onVolumes = LiveVolumes();

        // Named rather than counted. The first version of this counted two volumes on Off and
        // said nothing about what they were, which is exactly the sort of number you can stare
        // at for ten minutes - printing what it actually found named the culprit immediately.
        log.AppendLine($"  ..    preset {GameSettings.Shaders} | motion blur {GameSettings.MotionBlur}");

        Check(offVolumes == "none" && onVolumes == "~ShaderVolume",
              "the preset is the only thing driving the picture",
              $"Off: {offVolumes} | Overripe: {onVolumes}");

        GameSettings.SetShaders(before);
        GameSettings.SetMotionBlur(motionBlurBefore);
        GameSettings.SetPsxFilter(psxBefore);
        yield return null;

        // ---- sensitivity is read from settings rather than the prefab ----
        float sensitivity = GameSettings.Sensitivity;
        GameSettings.SetSensitivity(9.5f);

        // Read the real key directly - it must not have moved. Before the probe used its own
        // prefs namespace, this exact write landed on the prefs normal Play Mode uses.
        float realSensitivity = PlayerPrefs.GetFloat("gw_Sensitivity", -1f);
        Check(!Mathf.Approximately(realSensitivity, 9.5f), "probe writes stay out of your real settings",
              $"real gw_Sensitivity = {realSensitivity:F2}");

        Check(Mathf.Approximately(GameSettings.Sensitivity, 9.5f), "sensitivity takes a new value",
              GameSettings.Sensitivity.ToString("F2"));

        // Saved and reloaded, because a setting that only lives in memory is a setting that
        // resets every time the game starts - which is how it behaved before any of this.
        GameSettings.Load();
        Check(Mathf.Approximately(GameSettings.Sensitivity, 9.5f), "and survives a reload",
              GameSettings.Sensitivity.ToString("F2"));

        GameSettings.SetSensitivity(sensitivity);

        // ---- the aim page's reset covers everything on the aim page ----
        // AimToggle was saved and loaded but missing from the reset list, so "reset aim" and
        // "reset all" both left press-to-aim switched on.
        GameSettings.SetAimToggle(true);
        GameSettings.ResetAim();
        AudioListener.volume = 0f;

        Check(!GameSettings.AimToggle, "resetting aim turns press-to-aim back off",
              GameSettings.AimToggle ? "still on" : "off");

        // ---- the field of view reaches the camera ----
        if (camera != null)
        {
            float fov = GameSettings.Fov;
            GameSettings.SetFov(105f);
            yield return null;

            // Only meaningful while hip fired - aiming overrides the field of view entirely.
            Check(Mathf.Abs(camera.fieldOfView - 105f) < 1.5f, "field of view reaches the camera",
                  camera.fieldOfView.ToString("F0"));

            GameSettings.SetFov(fov);
            yield return null;
        }

        // ---- rebinding ----
        KeyCode jump = KeyBinds.Get(KeyBinds.Action.Jump);
        KeyBinds.Set(KeyBinds.Action.Jump, KeyCode.V);

        Check(KeyBinds.Get(KeyBinds.Action.Jump) == KeyCode.V, "a key can be rebound",
              KeyBinds.Get(KeyBinds.Action.Jump).ToString());

        // Binding one action onto another's key has to take it away from the first, or you get
        // a key that does two things and no way to tell which you meant.
        KeyBinds.Set(KeyBinds.Action.Fire, KeyCode.V);

        Check(KeyBinds.Get(KeyBinds.Action.Jump) == KeyCode.None,
              "a stolen key is taken off the old action",
              KeyBinds.Get(KeyBinds.Action.Jump).ToString());

        // The menu key refuses to move, because losing it means losing the only way back to the
        // screen that could put it right.
        KeyCode menu = KeyBinds.Get(KeyBinds.Action.Menu);
        KeyBinds.Set(KeyBinds.Action.Menu, KeyCode.Z);

        Check(KeyBinds.Get(KeyBinds.Action.Menu) == menu, "the menu key cannot be unbound",
              KeyBinds.Get(KeyBinds.Action.Menu).ToString());

        KeyBinds.ResetAll();

        Check(KeyBinds.Get(KeyBinds.Action.Jump) == jump && KeyBinds.Get(KeyBinds.Action.Fire) == KeyCode.Mouse0,
              "defaults come back", $"jump {KeyBinds.Get(KeyBinds.Action.Jump)}");

        // ---- the crosshair obeys its settings ----
        GameHud hud = GameHud.Instance;

        if (hud != null)
        {
            RectTransform tick = Get<RectTransform>(hud, "crosshairUp");

            GameSettings.SetCrosshairThickness(7f);
            GameSettings.SetCrosshairSize(21f);
            yield return null;
            yield return null;

            // Which axis is which depends on the reticle: a cross mark stands along the radius
            // and a triangle mark lies across it, so a shotgun legitimately reports the two
            // swapped. The setting is respected either way, and that is what this is asking -
            // the first version pinned the axes and failed the moment a weapon used a triangle.
            Vector2 size = tick != null ? tick.sizeDelta : Vector2.zero;
            float thin = Mathf.Min(size.x, size.y);
            float longest = Mathf.Max(size.x, size.y);

            Check(tick != null && Mathf.Abs(thin - 7f) < 0.01f && Mathf.Abs(longest - 21f) < 0.01f,
                  "the crosshair takes its size from settings",
                  tick == null ? "no tick" : $"{thin} thick, {longest} long");

            Image dot = Get<Image>(hud, "crosshairDot");
            GameSettings.SetCrosshairDot(true);
            yield return null;
            yield return null;

            Check(dot != null && dot.gameObject.activeSelf, "the centre dot can be switched on",
                  dot == null ? "no dot" : dot.gameObject.activeSelf ? "on" : "still off");

            GameSettings.SetCrosshairDot(false);
        }

        GameSettings.ResetAll();

        // ResetAll reloads, and reloading applies the master volume - re-mute, or the rest of
        // the probe plays out loud, the exact thing Start's mute exists to stop.
        AudioListener.volume = 0f;
        yield return null;
    }

    /// <summary>
    /// An unrelated setting must not rebuild post-processing. GameSettings.Changed fires on
    /// every setter, and rebuilding on each meant every tick of a slider drag threw the whole
    /// post stack away and made it again.
    /// </summary>
    IEnumerator CheckUnrelatedSettingsDontRebuildShaders()
    {
        // Settle first - the check before this one rebuilds the stack on its way out, and Find
        // can hand back the old volume while its Destroy is still pending, which then reads as
        // null a frame later whatever this check is actually testing.
        yield return null;
        yield return null;

        GameObject before = GameObject.Find("~ShaderVolume");
        bool hadVolume = before != null;
        float sensitivity = GameSettings.Sensitivity;

        GameSettings.SetSensitivity(sensitivity + 0.5f);
        yield return null;
        yield return null;

        GameObject after = GameObject.Find("~ShaderVolume");
        bool same = hadVolume && before != null && before == after;

        Check(same, "a sensitivity change leaves the post stack alone",
              !hadVolume ? "no volume to compare against" : same ? "same volume" : "volume was rebuilt");

        GameSettings.SetSensitivity(sensitivity);
        yield return null;
    }

    /// <summary>
    /// MinionsArt's grass (Assets/Grass) driven by GrassField: it has to have grown, be drawing
    /// something from where you stand, and have your own feet registered to push it. The shots
    /// are for a person to judge - height against the gorilla, how it bends underfoot.
    /// </summary>
    IEnumerator CheckGrass()
    {
        yield return null;
        yield return null;

        GrassField field = GrassField.Instance;
        GrassComputeScript compute = field != null ? field.GetComponent<GrassComputeScript>() : null;

        Check(field != null && field.PointCount > 10000, "the grass grew",
              field == null ? "no GrassField in the scene" : $"{field.PointCount} points");

        Check(compute != null && compute.IsInitialized, "the grass set itself up on the GPU",
              compute == null ? "no GrassComputeScript" : compute.IsInitialized ? "buffers built" : "never initialized");

        Check(compute != null && compute.VisibleCount > 0, "some grass is in view",
              compute == null ? "-" : $"{compute.VisibleCount} points past the culling tree");

        PlayerController player = LocalPlayer();
        ShaderInteractor feet = null;

        foreach (ShaderInteractor interactor in ShaderInteractor.Active)
        {
            if (player != null && interactor.transform.IsChildOf(player.transform))
                feet = interactor;
        }

        float feetHeight = 0f;
        if (feet != null && Physics.Raycast(feet.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 3f, 1))
            feetHeight = feet.transform.position.y - ground.point.y;

        Check(feet != null && Mathf.Abs(feetHeight) < 0.25f, "your feet push the grass",
              feet == null ? "no interactor on the local player" : $"interactor {feetHeight:F2} m above the ground, radius {feet.radius}");

        Camera camera = PlayerController.LocalCamera;
        if (camera != null)
        {
            // Rotated and captured in the same frame - set it and wait a frame, and the player's own
            // look code has put it back before the render (which is what flattened "map-floor").
            Quaternion was = camera.transform.rotation;

            camera.transform.rotation = Quaternion.Euler(0f, was.eulerAngles.y, 0f);
            Capture(null, "grass-eye");

            camera.transform.rotation = Quaternion.Euler(60f, was.eulerAngles.y, 0f);
            Capture(null, "grass-feet");

            camera.transform.rotation = was;
        }
    }

    /// <summary>
    /// Reported: after a slide the camera stayed down for good - gun at the top of the screen,
    /// looking up at everyone. Juice (the screen shake) is only created on the first shot or hit
    /// of a session, and it remembered whatever the camera's position was that frame as "rest" -
    /// mid-slide, that's the dipped one, and it put the camera back there every frame afterwards.
    ///
    /// Reproduced here by slide-then-first-shake: the ~Juice object is thrown away mid-slide so
    /// the next shake creates it again at exactly that moment. Measured at the end of the frame,
    /// after every LateUpdate, which is what actually gets drawn.
    /// </summary>
    IEnumerator CheckCameraRecoversFromSlide()
    {
        PlayerController player = LocalPlayer();
        PlayerMovement movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        Camera camera = PlayerController.LocalCamera;

        if (movement == null || camera == null)
        {
            Check(false, "the view comes back up after a slide", "no local player, movement or camera");
            yield break;
        }

        // Not WaitForEndOfFrame - it never resumes in batch mode (no game view to finish drawing),
        // which wedged the first version of this check. A recorder that runs after every other
        // LateUpdate sees the pose that actually gets drawn.
        FinalPoseRecorder recorder = gameObject.AddComponent<FinalPoseRecorder>();
        recorder.target = camera.transform;

        // Settled first - a shake left over from an earlier check would be baked into "rest".
        // Real time and the shake itself rather than a frame count: batch mode runs frames
        // unthrottled, so ninety of them can be under a tenth of a second.
        yield return Until(() => Juice.Amount < 0.001f, "let any earlier shake settle");
        yield return new WaitForSecondsRealtime(0.3f);

        Vector3 rest = recorder.localPosition;
        Transform holder = camera.transform.parent;
        Vector3 holderRest = recorder.parentLocalPosition;

        // Real speed, then the slide key held - the same path a player takes into a slide.
        movement.AddImpulse(player.transform.forward * 11f);
        KeyBinds.HeldOverride.Add(KeyBinds.Action.Walk);

        yield return Until(() => movement.Sliding, "start a slide");

        yield return new WaitForSecondsRealtime(0.15f);

        // The first shake of a session, landing mid-slide.
        GameObject juice = GameObject.Find("~Juice");
        if (juice != null)
            Object.Destroy(juice);
        yield return null;
        Juice.Shake(0.5f);

        yield return null;
        yield return null;

        KeyBinds.HeldOverride.Remove(KeyBinds.Action.Walk);
        movement.ResetVelocity();

        yield return Until(() => !movement.Sliding && !movement.Crouching, "stand back up");

        // Long enough for every ease (stance, shake, lean) to settle several times over.
        yield return Until(() => Juice.Amount < 0.001f, "let the shake settle");
        yield return new WaitForSecondsRealtime(1.5f);

        Vector3 after = recorder.localPosition;
        Vector3 holderAfter = recorder.parentLocalPosition;
        Destroy(recorder);

        Check(holder != null && (after - rest).magnitude < 0.02f, "the view comes back up after a slide",
              $"camera {(after.y - rest.y):+0.00;-0.00} m from where it started");

        Check(holder != null && (holderAfter - holderRest).magnitude < 0.02f, "and so does the camera holder",
              $"holder {(holderAfter.y - holderRest.y):+0.00;-0.00} m from where it started");
    }

    /// <summary>
    /// Reported by players: "the grappling is fun but it makes you just go towards your grapple
    /// point, you dont swing like an actual gorilla". A branch 9m up and 6m ahead, fired at from
    /// the ground with the key held: the swing has to lift you off the floor, carry you on past
    /// the point under the branch rather than stopping at the branch, never stretch the rope, and
    /// let you go with the speed the swing had.
    /// </summary>
    IEnumerator CheckVineSwings()
    {
        PlayerController player = LocalPlayer();
        PlayerMovement movement = player != null ? player.GetComponent<PlayerMovement>() : null;
        VineGrapple vine = player != null ? player.GetComponent<VineGrapple>() : null;
        Camera camera = PlayerController.LocalCamera;

        if (movement == null || vine == null || camera == null)
        {
            Check(false, "the vine swings", "no local player, movement, vine or camera");
            yield break;
        }

        movement.ResetVelocity();
        yield return Until(() => movement.Grounded, "stand on the ground");

        // The clearest of four directions, so a tree in the way doesn't decide the result.
        Vector3 start = player.transform.position;
        Vector3 ahead = player.transform.forward;
        float bestClear = -1f;

        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, 90f * i, 0f) * player.transform.forward;
            float clear = Physics.SphereCast(start + Vector3.up * 2f, 0.8f, dir, out RaycastHit block, 16f,
                                             Hitbox.WorldMask, QueryTriggerInteraction.Ignore) ? block.distance : 16f;
            if (clear > bestClear)
            {
                bestClear = clear;
                ahead = dir;
            }
        }

        GameObject branch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        branch.name = "~ProbeBranch";
        branch.transform.position = start + ahead * 6f + Vector3.up * 9f;
        branch.transform.localScale = new Vector3(1.2f, 0.4f, 1.2f);
        Physics.SyncTransforms();

        // Aimed straight at it for the cast, the way a player lines up the crosshair.
        camera.transform.rotation = Quaternion.LookRotation(branch.transform.position - camera.transform.position);
        KeyBinds.HeldOverride.Add(KeyBinds.Action.Grapple);
        typeof(VineGrapple).GetMethod("TryAttach", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(vine, null);

        // Reported 2026-09-28: "the small jump you do when you initially grapple from the ground is
        // janky". It was an 8 m/s kick on the frame the rope caught; the takeoff is eased in now.
        float riseAtAttach = movement.Velocity.y;

        Check(vine.Attached, "the vine catches a branch", $"clear run {bestClear:F1}m");
        Check(riseAtAttach < 2f, "the swing lifts off smoothly, not with a hop",
              $"{riseAtAttach:F1} m/s upward the moment it caught");

        // Where the rope actually caught - a point on the branch's surface, not its centre.
        FieldInfo anchorField = typeof(VineGrapple).GetField("anchorWorldPoint", BindingFlags.NonPublic | BindingFlags.Instance);
        Vector3 anchor = anchorField != null ? (Vector3)anchorField.GetValue(vine) : branch.transform.position;

        Vector3 anchorFlat = new Vector3(anchor.x, 0f, anchor.z);
        float highest = start.y;
        float furthestPast = float.MinValue;
        float worstStretch = 0f;
        float closest = float.MaxValue;
        float until = Time.realtimeSinceStartup + 2.5f;

        FieldInfo lengthField = typeof(VineGrapple).GetField("ropeLength", BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo swingingField = typeof(VineGrapple).GetField("swinging", BindingFlags.NonPublic | BindingFlags.Instance);
        bool swung = swingingField != null && (bool)swingingField.GetValue(vine);

        float PastTheBranch() => Vector3.Dot(new Vector3(player.transform.position.x, 0f, player.transform.position.z)
                                             - anchorFlat, ahead);

        // Held until the swing has carried you just past the point under the branch - the bottom
        // of the arc, fastest, where a player lets go to fling forward.
        while (vine.Attached && Time.realtimeSinceStartup < until && PastTheBranch() < 0.5f)
        {
            yield return null;

            Vector3 at = player.transform.position;
            Vector3 pivot = anchor - Vector3.up * 1.3f;

            highest = Mathf.Max(highest, at.y);
            closest = Mathf.Min(closest, Vector3.Distance(at, anchor));
            furthestPast = Mathf.Max(furthestPast, PastTheBranch());

            if (lengthField != null)
                worstStretch = Mathf.Max(worstStretch, Vector3.Distance(at, pivot) - (float)lengthField.GetValue(vine));
        }

        Vector3 swingVelocity = movement.Velocity;

        KeyBinds.HeldOverride.Remove(KeyBinds.Action.Grapple);
        yield return null;
        yield return null;

        float releaseSpeed = movement.Velocity.magnitude;

        // Then the flight: let go at the bottom and you keep going.
        float flightUntil = Time.realtimeSinceStartup + 0.5f;
        while (Time.realtimeSinceStartup < flightUntil && !movement.Grounded)
        {
            furthestPast = Mathf.Max(furthestPast, PastTheBranch());
            yield return null;
        }

        Check(swung, "a branch with room under it is a swing, not a pull", swung ? "swing" : "pull");
        Check(highest - start.y > 1f, "the swing lifts you off the ground", $"{highest - start.y:F2}m up");
        Check(furthestPast > 1.5f, "the swing carries you past the point under the branch",
              $"{furthestPast:F2}m past it (a pull stops short of it)");
        Check(closest > 2f, "you swing under the branch, not into it", $"closest {closest:F2}m");
        Check(worstStretch < 0.05f, "the rope never stretches", $"worst {worstStretch:F3}m over its length");
        Check(!vine.Attached && releaseSpeed > 3f, "letting go keeps the swing's speed",
              $"{swingVelocity.magnitude:F1} m/s swinging, {releaseSpeed:F1} m/s after release");

        Object.Destroy(branch);
        movement.ResetVelocity();
        yield return Until(() => movement.Grounded, "land again");
    }

    /// Which of ShaderStack's volumes the PSX pass is in right now: "world", "gun camera", both,
    /// or "nowhere".
    static string PsxWhere()
    {
        bool world = VolumeHasPsx("~ShaderVolume");
        bool gun = VolumeHasPsx("~ViewModelShaderVolume");

        return world && gun ? "both" : world ? "world" : gun ? "gun camera" : "nowhere";
    }

    static bool VolumeHasPsx(string name)
    {
        GameObject host = GameObject.Find(name);
        UnityEngine.Rendering.PostProcessing.PostProcessVolume volume =
            host != null ? host.GetComponent<UnityEngine.Rendering.PostProcessing.PostProcessVolume>() : null;

        return volume != null && volume.enabled && volume.profile != null && volume.profile.HasSettings<PsxFilter>();
    }

    static string LiveVolumes()
    {
        List<string> names = new List<string>();

        foreach (var volume in Object.FindObjectsByType<UnityEngine.Rendering.PostProcessing.PostProcessVolume>(
                     FindObjectsSortMode.None))
        {
            if (volume.isActiveAndEnabled)
                names.Add(volume.name);
        }

        names.Sort();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>
    /// While the settings screen is up, the game underneath has to stop listening.
    ///
    /// Both halves of this were real: the camera turned with the mouse while you dragged a
    /// sensitivity slider, and clicking a button also pulled the trigger. Neither is the sort of
    /// thing a static check can see, because both are about which of two things is reading the
    /// same input.
    /// </summary>
    IEnumerator CheckSettingsScreenBlocksTheGame(PlayerController player)
    {
        SettingsMenu menu = SettingsMenu.Instance;

        if (menu == null)
        {
            Check(false, "the settings screen exists", "RoomManager never instantiated it");
            yield break;
        }

        menu.Open();
        yield return null;
        yield return null;

        Check(SettingsMenu.IsOpen, "the settings screen opens", "IsOpen");

        // The camera must not move. Driven by reading the angle across a few frames rather than
        // by faking mouse input, which the probe cannot do - if anything else moved the camera
        // this would catch that too, which is the point.
        Camera camera = PlayerController.LocalCamera;
        Quaternion before = camera != null ? camera.transform.rotation : Quaternion.identity;

        yield return null;
        yield return null;
        yield return null;

        float drift = camera != null ? Quaternion.Angle(before, camera.transform.rotation) : 0f;
        Check(drift < 0.01f, "the camera is still while settings are open", $"{drift:F3} degrees");

        Check(!PlayerController.CursorCaptured, "and the game lets go of the cursor",
              PlayerController.CursorCaptured ? "still captured" : "free");

        SingleShotGun gun = player.ActiveGun;
        int loaded = gun != null ? gun.Ammo : -1;

        // Standing still is part of it. Rebinding your movement keys means pressing them, and
        // every press was also walking you somewhere.
        Vector3 stood = player.transform.position;

        yield return null;
        yield return null;
        yield return null;

        float wandered = Vector3.Distance(stood, player.transform.position);
        Check(wandered < 0.05f, "and the player stays put", $"{wandered:F3}m");

        menu.Close();
        yield return null;
        yield return null;

        Check(!SettingsMenu.IsOpen, "it closes again", "IsOpen false");

        // Closing has to hand the mouse back on its own. It used to wait for a click, and that
        // click went through to the weapon.
        // Checked against the game's intent rather than Cursor.lockState, which does not report
        // anything useful with no graphics device - it reads None either way, so the version of
        // this that asked the OS was passing the "free" half for the wrong reason.
        Check(PlayerController.CursorCaptured, "closing recaptures the cursor",
              PlayerController.CursorCaptured ? "captured" : "still free");

        if (gun != null)
            Check(gun.Ammo == loaded, "and nothing was fired through the panel",
                  $"{loaded} rounds before, {gun.Ammo} after");
    }

    /// <summary>
    /// You cannot be shot for a moment after you land, and the moment ends when you shoot.
    ///
    /// Both halves matter. Immunity that never ends is a way to walk into a fight invulnerable,
    /// and immunity that does not exist is the spawn kill it was added to stop.
    /// </summary>
    IEnumerator CheckSpawnProtection(PlayerController player)
    {
        // Freshly spawned by this point in the run, so the shield should still be up.
        Check(player.IsProtected, "you land protected",
              player.IsProtected ? "immune" : "already exposed");

        int before = player.HealthPoints;
        player.TakeDamage(40f, "Rifle", false);

        yield return null;
        yield return null;

        Check(player.HealthPoints == before, "and nothing lands while it is up",
              $"{before} then {player.HealthPoints}");

        // Shooting is the clearest possible signal that you have stopped getting your bearings.
        player.DropProtection();
        yield return null;

        Check(!player.IsProtected, "shooting gives it up",
              player.IsProtected ? "still immune" : "exposed");

        player.TakeDamage(40f, "Rifle", false);

        yield return null;
        yield return null;

        Check(player.HealthPoints < before, "and then damage lands normally",
              $"{before} down to {player.HealthPoints}");

        player.Heal(999f);
        yield return null;

        // ---- and the HUD says which way it came from ----
        GameHud hud = GameHud.Instance;

        if (hud != null)
        {
            RectTransform ring = Get<RectTransform>(hud, "arrowContainer");

            if (ring != null)
            {
                hud.ShowDamageFrom(player.transform.position + player.transform.right * 8f);

                yield return null;
                yield return null;

                int lit = 0;

                foreach (Image mark in ring.GetComponentsInChildren<Image>(true))
                {
                    if (mark.gameObject.activeInHierarchy)
                        lit++;
                }

                Check(lit > 0, "a damage bearing reaches the screen", $"{lit} marks up");
            }
        }
    }

    /// <summary>
    /// How much of the body you can actually hit.
    ///
    /// Samples the skinned mesh and asks, for each vertex, whether any hitbox collider contains
    /// it. That turns "the hitboxes feel off" into a percentage, which is the only way to tell
    /// a fix from a change - and it points at *where* the holes are, which hand-tuning radii by
    /// eye never would.
    ///
    /// Convex colliders only, which spheres and capsules both are: ClosestPoint returns the
    /// point itself when it is inside, and something else when it is not.
    /// </summary>
    void ReportHitboxCoverage(PlayerController player)
    {
        Collider[] boxes = player.GetComponentsInChildren<Collider>(true);
        List<Collider> hit = new List<Collider>();

        foreach (Collider c in boxes)
        {
            if (c.GetComponent<Hitbox>() != null)
                hit.Add(c);
        }

        if (hit.Count == 0)
        {
            Check(false, "the body has hitboxes at all", "none found");
            return;
        }

        int inside = 0;
        int total = 0;
        Dictionary<string, int> missedNear = new Dictionary<string, int>();

        foreach (SkinnedMeshRenderer skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh baked = new Mesh();
            skin.BakeMesh(baked, true);

            Vector3[] verts = baked.vertices;

            // Every eleventh vertex. The mesh has thousands and the answer does not change.
            for (int i = 0; i < verts.Length; i += 11)
            {
                Vector3 world = skin.transform.TransformPoint(verts[i]);
                total++;

                bool covered = false;
                float nearest = float.MaxValue;
                string nearestName = "?";

                foreach (Collider c in hit)
                {
                    Vector3 closest = c.ClosestPoint(world);
                    float gap = Vector3.Distance(closest, world);

                    if (gap < 0.001f)
                    {
                        covered = true;
                        break;
                    }

                    if (gap < nearest)
                    {
                        nearest = gap;
                        nearestName = c.name;
                    }
                }

                if (covered)
                {
                    inside++;
                }
                else
                {
                    missedNear.TryGetValue(nearestName, out int count);
                    missedNear[nearestName] = count + 1;
                }
            }

            Object.DestroyImmediate(baked);
        }

        float coverage = total > 0 ? inside / (float)total : 0f;

        // Where the holes are, biggest first. This is the part that tells you what to build.
        List<KeyValuePair<string, int>> holes = new List<KeyValuePair<string, int>>(missedNear);
        holes.Sort((a, b) => b.Value.CompareTo(a.Value));

        System.Text.StringBuilder where = new System.Text.StringBuilder();

        for (int i = 0; i < holes.Count && i < 4; i++)
            where.Append($"{holes[i].Key.Replace("hitbox_", "")}:{holes[i].Value} ");

        log.AppendLine($"  ..    hitbox coverage {coverage:P0} of {total} sampled points"
                       + $" | uncovered nearest to {where}");

        // Two thirds is not a target, it is a floor. Below that and shots are visibly passing
        // through people, which is what started this.
        Check(coverage > 0.66f, "most of the body can be hit", $"{coverage:P0}");
    }

    /// <summary>
    /// Arriving in a match already in progress has to leave you holding something.
    ///
    /// The master issues loadouts when somebody joins the room, but that fires the moment they
    /// connect - well before they have loaded the map and spawned a body. So the interesting
    /// case is a player who builds their weapons with no loadout property at all, and then
    /// receives one a moment later. Both halves have to work: the build has to arm them with
    /// something, and the late property has to replace it.
    /// </summary>
    /// <summary>
    /// Reported directly as "the text is not on the scoreboard backdrop" - an overlay canvas, so
    /// no camera render can show it and no screenshot can confirm or deny that report. This is
    /// the "read it back" equivalent for a layout claim rather than a text claim: real,
    /// laid-out world corners off both RectTransforms after an actual frame, rather than the
    /// hand-worked arithmetic that produced the numbers in the first place and could easily be
    /// wrong the same way twice.
    /// </summary>
    IEnumerator CheckScoreboardLayout()
    {
        Scoreboard board = Object.FindFirstObjectByType<Scoreboard>(FindObjectsInactive.Include);

        if (board == null)
        {
            Check(false, "the scoreboard is in the scene", "no Scoreboard component");
            yield break;
        }

        Scoreboard.OpenOverride = true;

        // A few real frames - the layout groups and the canvas both need at least one pass to
        // settle before GetWorldCorners means anything.
        for (int i = 0; i < 3; i++)
            yield return null;

        Canvas.ForceUpdateCanvases();

        RectTransform backdrop = board.BackdropRect;
        RectTransform rows = board.ContainerRect;

        if (backdrop == null || rows == null)
        {
            Check(false, "the scoreboard has a backdrop and a row container",
                  $"backdrop={(backdrop != null)} rows={(rows != null)}");
            Scoreboard.OpenOverride = null;
            yield break;
        }

        Vector3[] backdropCorners = new Vector3[4];
        Vector3[] rowsCorners = new Vector3[4];
        backdrop.GetWorldCorners(backdropCorners);
        rows.GetWorldCorners(rowsCorners);

        // corners[0] is bottom-left, corners[2] is top-right, for both.
        bool insideX = rowsCorners[0].x >= backdropCorners[0].x && rowsCorners[2].x <= backdropCorners[2].x;
        bool insideY = rowsCorners[0].y >= backdropCorners[0].y && rowsCorners[2].y <= backdropCorners[2].y;

        Check(insideX && insideY, "the row column sits inside the backdrop",
              $"backdrop {backdropCorners[0]}..{backdropCorners[2]}, rows {rowsCorners[0]}..{rowsCorners[2]}");

        // Rows are a five-column table now (Rank/Name/Primary/Secondary/Streak), not one
        // TMP_Text per row - index 0 is always the column-header row Refresh() writes first, so
        // the first real player is index 1. Read its Name column specifically, confirming
        // Refresh() actually populated a player rather than just that the empty template exists.
        TMP_Text firstRowName = null;

        foreach (Transform child in rows)
        {
            if (!child.gameObject.activeSelf)
                continue;

            TMP_Text candidate = child.Find("Name")?.GetComponent<TMP_Text>();

            if (candidate != null && candidate.text != "NAME")
            {
                firstRowName = candidate;
                break;
            }
        }

        Check(firstRowName != null && !string.IsNullOrWhiteSpace(firstRowName.text),
              "the scoreboard actually filled in a row",
              firstRowName == null ? "no active player row" : $"reads '{firstRowName.text}'");

        if (firstRowName != null)
        {
            Check(!firstRowName.richText && firstRowName.overflowMode == TextOverflowModes.Ellipsis,
                  "names aren't parsed as rich text and cut off cleanly",
                  $"richText={firstRowName.richText} overflow={firstRowName.overflowMode}");
        }

        if (firstRowName != null)
        {
            RectTransform rowRect = (RectTransform)firstRowName.transform.parent;
            Vector3[] rowCorners = new Vector3[4];
            rowRect.GetWorldCorners(rowCorners);

            bool rowInsideX = rowCorners[0].x >= backdropCorners[0].x && rowCorners[2].x <= backdropCorners[2].x;
            bool rowInsideY = rowCorners[0].y >= backdropCorners[0].y && rowCorners[2].y <= backdropCorners[2].y;

            Check(rowInsideX && rowInsideY, "that row sits inside the backdrop",
                  $"backdrop {backdropCorners[0]}..{backdropCorners[2]}, row {rowCorners[0]}..{rowCorners[2]}");
        }

        Scoreboard.OpenOverride = null;
    }

    IEnumerator CheckLateJoinGetsWeapons(PlayerController player)
    {
        // Wipe it, the way a fresh arrival has nothing.
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { PlayerController.LoadoutKey, string.Empty },
        });

        yield return null;
        yield return null;

        // Rebuild from that empty state, which is what spawning before the property lands does.
        typeof(PlayerController)
            .GetMethod("BuildLoadout", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(player, null);

        yield return null;

        int armed = player.GetComponentsInChildren<SingleShotGun>(true).Length;
        Check(armed > 0, "spawning before your loadout arrives still arms you", $"{armed} weapons");

        SingleShotGun holding = player.ActiveGun;
        Check(holding != null, "and one of them is actually in your hands",
              holding != null ? holding.name : "nothing equipped");

        // Now the master's answer turns up late, exactly as it does over a real connection.
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
        {
            { PlayerController.LoadoutKey, MatchState.Rules.Serialise(new[] { "Sniper" }) },
        });

        yield return Until(() => PlayerController.LoadoutFor(PhotonNetwork.LocalPlayer)[0] == "Sniper",
                           "receive the loadout the master sent");

        yield return null;
        yield return null;

        SingleShotGun after = player.ActiveGun;
        Check(after != null && after.name == "Sniper",
              "and a late loadout replaces what you were given",
              after == null ? "nothing equipped" : after.name);
    }

    // Leaving a room and rejoining it cannot be checked here, and it is worth saying why rather
    // than leaving a gap. Offline mode has no server: leaving destroys the only room that
    // exists, and returning to the menu makes the Launcher reconnect, which tears down anything
    // staged afterwards. A test that works around all of that is testing the workaround.
    //
    // What the rejoin path has instead is diagnostics. RoomManager says which of the four
    // conditions a stuck spawn is waiting on, which turns "I rejoined and had nothing" into a
    // line naming the cause.

    /// The scope overlay is a generated texture: opaque outside a circle, clear inside. Can't
    /// be seen composited, but it can be checked on its own and written out to look at.
    void CheckScopeMask()
    {
        const int size = 256;
        Texture2D mask = GameHud.BuildScopeMask(size);

        if (mask == null)
        {
            Check(false, "the scope mask builds", "null");
            return;
        }

        float middle = mask.GetPixel(size / 2, size / 2).a;
        float corner = mask.GetPixel(2, 2).a;

        // The very midpoint of an edge. The circle is inscribed in the square, so a point a few
        // pixels in from here is still inside the glass - which is what the first version of
        // this sampled, and it read the vignette as a hole in the rim.
        float edge = mask.GetPixel(size / 2, 0).a;

        Check(middle < 0.05f, "you can see through the middle of the scope", $"alpha {middle:F2}");
        Check(corner > 0.95f, "the corners are blacked out", $"alpha {corner:F2}");
        Check(edge > 0.95f, "the rim is closed all the way round", $"alpha {edge:F2}");

        System.IO.Directory.CreateDirectory(ShotFolder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ShotFolder, "scope-mask.png"), mask.EncodeToPNG());

        Object.DestroyImmediate(mask);
    }

    /// <summary>
    /// Reported directly as "does nothing." Unlike the scoreboard, this one's a camera effect, so
    /// it can be checked by rendering to a texture - a real before/after pixel comparison rather than trusting that "it
    /// compiled and the profile has the setting in it" means the picture actually changed.
    ///
    /// Renders the composited frame (world camera, then the gun camera on top, the way a real
    /// frame is built) and the gun camera alone. The gun is drawn by ViewModelCamera's second
    /// camera after the world's post stack has already run, so a PSX pass on the world camera
    /// never reached it - the frame changed, the gun didn't.
    /// </summary>
    IEnumerator CheckPsxFilterVisiblyChangesTheImage()
    {
        // Always the Bunch. This used to measure whatever the random loadout had handed out, and
        // the thresholds below were set against bananas - once the flat purple grapes joined the
        // pool, a run that happened to hold them moved the gun's pixels less and failed a check
        // about the filter, not the weapon.
        PlayerController.PublishLoadout(new[] { "Rifle" });
        yield return null;
        yield return null;

        Camera world = PlayerController.LocalCamera;
        ViewModelCamera viewModel = world != null ? world.GetComponent<ViewModelCamera>() : null;
        Camera weapon = viewModel != null ? viewModel.WeaponCamera : null;

        if (world == null || weapon == null
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Check(false, "psx filter changes the render", "no world camera, weapon camera or graphics device");
            yield break;
        }

        GameSettings.ShaderPreset presetBefore = GameSettings.Shaders;
        bool psxBefore = GameSettings.PsxFilter;

        GameSettings.SetPsxFilter(false);
        yield return null;
        yield return null;

        Color32[] offFrame = ReadPixels(world, weapon);
        Color32[] offGun = ReadPixels(weapon);

        GameSettings.SetPsxFilter(true);
        yield return null;
        yield return null;

        Color32[] onFrame = ReadPixels(world, weapon);
        Color32[] onGun = ReadPixels(weapon);

        SavePixels(offFrame, "psx-composite-off.png");
        SavePixels(onFrame, "psx-composite-on.png");

        // The gun-alone comparison means nothing if there's no gun in frame to compare.
        int litGunPixels = 0;
        foreach (Color32 p in offGun)
        {
            if (p.r + p.g + p.b > 24)
                litGunPixels++;
        }

        Check(litGunPixels > offGun.Length / 100, "the gun is in frame for the psx check",
              $"{litGunPixels} lit pixels of {offGun.Length}");

        // A real quantize-and-dither pass moves plenty of pixels by several levels each: this is
        // a low bar (any of them meaningfully move at all), not a claim about how strong the
        // effect looks - that's still a taste call for a real render, not this check.
        double frameDiff = AverageDifference(offFrame, onFrame);
        Check(frameDiff > 0.5, "the psx filter visibly changes the render",
              $"average per-channel difference {frameDiff:F2} across the finished frame");

        // Averaged over the gun's own pixels, not the whole frame - over the whole frame the number
        // mostly measured how much of the screen the gun covered (0.92 for one weapon, 0.42 for a
        // smaller one, the same effect both times).
        double gunDiff = AverageDifference(offGun, onGun, lit: 24);
        Check(gunDiff > 3.0, "the psx filter reaches the gun too",
              $"average per-channel difference {gunDiff:F2} across the gun's own pixels");

        GameSettings.SetPsxFilter(psxBefore);
        GameSettings.SetShaders(presetBefore);
    }

    const int ReadWidth = 480;
    const int ReadHeight = 270;

    /// Average per-channel difference. With lit set, only over pixels brighter than that in either
    /// image - the gun against a black background, rather than the background too.
    static double AverageDifference(Color32[] a, Color32[] b, int lit = -1)
    {
        long diff = 0;
        long counted = 0;

        for (int i = 0; i < a.Length; i++)
        {
            if (lit >= 0 && a[i].r + a[i].g + a[i].b <= lit && b[i].r + b[i].g + b[i].b <= lit)
                continue;

            diff += System.Math.Abs(a[i].r - b[i].r) + System.Math.Abs(a[i].g - b[i].g) + System.Math.Abs(a[i].b - b[i].b);
            counted++;
        }

        return counted == 0 ? 0 : diff / (double)(counted * 3);
    }

    /// Renders each camera in order into one black-cleared target - world first, then the gun
    /// camera on top of it, the same way a real frame is composited.
    static Color32[] ReadPixels(params Camera[] cameras) => ReadPixels(ReadWidth, ReadHeight, cameras);

    static Color32[] ReadPixels(int width, int height, params Camera[] cameras)
    {
        RenderTexture target = new RenderTexture(width, height, 24);
        RenderTexture previousActive = RenderTexture.active;

        RenderTexture.active = target;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previousActive;

        foreach (Camera camera in cameras)
        {
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previousTarget;
        }

        RenderTexture.active = target;
        Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();
        RenderTexture.active = previousActive;

        Color32[] pixels = shot.GetPixels32();

        Object.DestroyImmediate(shot);
        target.Release();
        Object.DestroyImmediate(target);

        return pixels;
    }

    static void SavePixels(Color32[] pixels, string name, int width = ReadWidth, int height = ReadHeight)
    {
        Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        shot.SetPixels32(pixels);
        shot.Apply();
        System.IO.Directory.CreateDirectory(ShotFolder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ShotFolder, name), shot.EncodeToPNG());
        Object.DestroyImmediate(shot);
    }

    // Renders whatever the player is looking at to a PNG next to the log.
    void Capture(PlayerController ignored, string name) => CaptureWith(PlayerController.LocalCamera, name);

    /// <summary>
    /// The world and the gun together, the way a real frame is put together. Capture renders the
    /// world camera alone, and the weapon is on its own camera - so the viewmodel shots this probe
    /// had been taking since the second camera went in never had a weapon in them.
    /// </summary>
    void CaptureComposite(string name)
    {
        Camera world = PlayerController.LocalCamera;
        ViewModelCamera viewModel = world != null ? world.GetComponent<ViewModelCamera>() : null;
        Camera weapon = viewModel != null ? viewModel.WeaponCamera : null;

        if (world == null || weapon == null
            || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            log.AppendLine($"  ..    screenshot '{name}'                             skipped, no cameras or graphics device");
            return;
        }

        const int width = 960;
        const int height = 540;

        SavePixels(ReadPixels(width, height, world, weapon), name + ".png", width, height);
        log.AppendLine($"  ..    screenshot '{name}'                             {System.IO.Path.Combine(ShotFolder, name + ".png")}");
    }

    void CaptureWith(Camera camera, string name)
    {
        if (camera == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            log.AppendLine($"  ..    screenshot '{name}'                             skipped, no graphics device");
            return;
        }

        const int width = 960;
        const int height = 540;

        RenderTexture target = new RenderTexture(width, height, 24);
        RenderTexture previous = camera.targetTexture;

        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = previous;

        RenderTexture wasActive = RenderTexture.active;
        RenderTexture.active = target;

        Texture2D shot = new Texture2D(width, height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();

        RenderTexture.active = wasActive;

        string path = System.IO.Path.Combine(ShotFolder, name + ".png");
        System.IO.Directory.CreateDirectory(ShotFolder);
        System.IO.File.WriteAllBytes(path, shot.EncodeToPNG());

        Object.DestroyImmediate(shot);
        target.Release();
        Object.DestroyImmediate(target);

        log.AppendLine($"  ..    screenshot '{name}'                             {path}");
    }

    static string ShotFolder =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Logs", "probe-shots");

    /// The probe's matches are six seconds long and roll over on their own, and a rollover
    /// resets stats and reissues loadouts. Anything that needs the match to hold still waits here.
    IEnumerator LiveWithTimeLeft(float seconds)
    {
        yield return Until(() => MatchState.Phase == MatchPhase.Live && MatchState.TimeLeft > seconds,
                           $"a live match with {seconds:F1}s left");
    }

    IEnumerator Until(System.Func<bool> condition, string what)
    {
        float deadline = Time.realtimeSinceStartup + StepTimeout;

        while (!condition())
        {
            if (Time.realtimeSinceStartup > deadline)
            {
                Check(false, $"waiting to {what}", $"timed out after {StepTimeout}s");
                yield break;
            }

            yield return null;
        }
    }
}
