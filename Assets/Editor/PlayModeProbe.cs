using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
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

public class ProbeRunner : MonoBehaviour
{
    const float StepTimeout = 20f;

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

        // A probe that hangs is worse than one that fails - it looks like it's still working.
        startedAt = Time.realtimeSinceStartup;

        yield return RunProbe();

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

        PhotonNetwork.LoadLevel(1);
        yield return Until(() => UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == 1,
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

        CheckWeapons(player);
        CheckHitboxes(player);
        ReportScales(player);

        // The one thing no amount of measuring settles: what it actually looks like down the
        // barrel. One shot per weapon, because they are wildly different lengths and a framing
        // that suits the pistol can put the sniper straight through the crosshair.
        yield return CaptureEveryWeapon(player);

        // ---- match clock ----
        Check(MatchState.Phase == MatchPhase.Warmup, "a match starts in warmup", MatchState.Phase.ToString());

        float warmupLeft = MatchState.TimeLeft;
        Check(warmupLeft > 0f, "the clock is running", $"{warmupLeft:F1}s left");

        yield return Until(() => MatchState.Phase == MatchPhase.Live, "go live");
        Check(MatchState.Phase == MatchPhase.Live, "warmup becomes live on its own", MatchState.Phase.ToString());

        // ---- switching mode has to reissue weapons ----
        yield return CheckModeChangeReissuesLoadouts();

        // ---- joining and leaving ----
        yield return CheckJoinAndLeaveMessages();

        // ---- the HUD is showing what the game thinks is true ----
        yield return CheckHudReadsTheGame(player);

        // ---- a loadout that resolves to nothing still arms you ----
        yield return CheckEmptyLoadoutFallsBack(player);

        // ---- hitstop ----
        yield return CheckHitstopRestores();

        // ---- firing ----
        yield return CheckFiringLeavesAStreak(player);

        // ---- aiming down the banana ----
        yield return CheckAimingDownSights(player);

        // ---- what an enemy looks like ----
        // Two weapons, because the pose is different: a pistol is one fist, everything longer
        // wants a second hand on it.
        yield return CheckEnemyIsVisible(player, "Rifle");
        yield return CheckEnemyIsVisible(player, "Pistol");

        // ---- dying ----
        yield return CheckDeathAndRespawn();

        // ---- gun game hands out one weapon ----
        yield return CheckGunGameLoadout();
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

    // Right click on Big Mike. Driven straight through UpdateAim, because batch mode has no
    // mouse and this is the only way to see whether any of it moves.
    IEnumerator CheckAimingDownSights(PlayerController player)
    {
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
        }

        yield return null;
        yield return null;

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

    IEnumerator CheckDeathAndRespawn()
    {
        PlayerController player = LocalPlayer();
        if (player == null)
        {
            Check(false, "a player to kill", "none");
            yield break;
        }

        int feedBefore = MatchState.Feed.Count;

        // Straight through the interface a bullet uses, so this exercises the real path.
        player.TakeDamage(500f, "Pistol", true);

        yield return Until(() => RoomManager.AwaitingRespawn, "register the death");
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

        yield return Until(() => LocalPlayer() != null, "respawn");

        PlayerController respawned = LocalPlayer();
        Check(respawned != null, "you come back", respawned != null ? "new controller" : "never respawned");
        Check(!RoomManager.AwaitingRespawn, "the respawn timer cleared", "cleared");

        yield return null;
        yield return null;

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

        foreach (string weapon in WeaponLoadout.AllWeapons)
        {
            PlayerController.PublishLoadout(new[] { weapon });

            // A frame for the property callback, a frame for the rebuild to settle.
            yield return null;
            yield return null;

            ReportViewport(player, weapon);
            Capture(player, "viewmodel-" + weapon.ToLower());
        }
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

    // Renders whatever the player is looking at to a PNG next to the log.
    void Capture(PlayerController ignored, string name)
    {
        Camera camera = PlayerController.LocalCamera;
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
