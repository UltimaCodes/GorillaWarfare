using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
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
        CheckArms(player);
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

        // ---- what an enemy looks like ----
        yield return CheckEnemyIsVisible(player);

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

        string[] expected = MatchState.RolledWeapons;

        built.Sort();
        List<string> want = new List<string>(expected);
        want.Sort();

        Check(built.Count == expected.Length, "the loadout is the size the match rolled",
              $"{built.Count} built, {expected.Length} rolled");

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

    // The one that has been silently broken the whole time.
    void CheckArms(PlayerController player)
    {
        Transform holder = Holder(player);
        if (holder == null)
            return;

        bool found = false;
        foreach (Transform child in holder)
        {
            if (child.name.StartsWith("ViewArms"))
                found = true;
        }

        Check(found, "the first person arms survived the loadout",
              found ? "still parented to the holder" : "destroyed - the hands will be invisible");
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
    IEnumerator CheckEnemyIsVisible(PlayerController player)
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
            GameObject held = new GameObject("Rifle");
            held.transform.SetParent(hand, false);
            Hitbox.Neutralise(held.transform);

            SingleShotGun gun = held.AddComponent<SingleShotGun>();
            gun.Configure(Resources.Load<GunInfo>("Guns/Rifle"), null, false);

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

        Capture(null, "enemy");

        Object.DestroyImmediate(stand);
        yield return null;
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
            CheckArms(respawned);
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

        CheckArms(player);
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
            float nearest = cam.WorldToViewportPoint(b.center - cam.transform.forward * b.extents.magnitude).z;

            log.AppendLine($"  ..    {weapon,-9} '{child.name,-16}' centre {centre.x:F2},{centre.y:F2} depth {centre.z:F2} nearest {nearest:F2} len {b.size.magnitude:F2}");
        }
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
