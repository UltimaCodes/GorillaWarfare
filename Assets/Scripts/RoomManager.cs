using System.Collections;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;

// Spawning and player stats. Keeping the RoomManager name so the object already sitting in the
// menu scene keeps working, even though it does a fair bit more than manage the room now.
//
// Rewritten to match how Photon's own OnJoinedInstantiate does it (see
// Photon/PhotonUnityNetworking/UtilityScripts/Prototyping/OnJoinedInstantiate.cs):
// spawn straight from OnJoinedRoom, one flat Instantiate, no intermediate networked object.
//
// The old setup went RoomManager -> spawn a PlayerManager over the network -> PlayerManager
// spawns the PlayerController, passing its own ViewID through InstantiationData so the
// controller could find it again with PhotonView.Find. That's three things that have to line up
// across every client in the right order, and PlayerManager existed only to hold two ints that
// were already being replicated as custom properties anyway. Gone.
public class RoomManager : MonoBehaviourPunCallbacks
{
    public static RoomManager Instance;

    const string playerPrefab = "PhotonPrefabs/PlayerController";
    const int gameSceneIndex = 1;

    public const string KillsKey = "kills";
    public const string DeathsKey = "deaths";

    GameObject localController;
    GameObject spectatorCamera;
    Coroutine spawnRoutine;
    Coroutine deathRoutine;

    /// True between dying and respawning. The HUD reads it to draw the countdown.
    public static bool AwaitingRespawn { get; private set; }

    /// When the respawn happens, in Time.time. Only meaningful while AwaitingRespawn.
    public static float RespawnAt { get; private set; }

    // Fallback in case someone opens the game scene directly without going via the menu.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        if (Instance != null)
            return;

        if (FindFirstObjectByType<RoomManager>() != null)
            return;

        GameObject host = new GameObject("RoomManager");
        host.AddComponent<RoomManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        Instance = this;

        // Built here rather than dropped in a scene so there is nothing to wire up and no
        // serialized copy to drift from the code. Both live on the one object that survives
        // the trip between the menu and the game.
        if (GetComponent<MatchState>() == null)
            gameObject.AddComponent<MatchState>();

        // Here rather than in a scene, because a music player that reloads with the scene
        // restarts the track every time you join a room.
        if (GetComponent<MusicPlayer>() == null)
            gameObject.AddComponent<MusicPlayer>();
    }

    public override void OnEnable()
    {
        base.OnEnable();

        // Destroy is deferred, so a duplicate killed in Awake still runs OnEnable. Without this
        // it subscribes too and you end up spawning twice.
        if (Instance != this)
            return;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Two entry points because the order differs depending on how you got here. The host joins
    // the room and loads the scene afterwards; a late joiner has the scene pushed at them by
    // AutomaticallySyncScene and may already be in the room. Whichever fires last does the work,
    // and TrySpawn is idempotent.
    public override void OnJoinedRoom()
    {
        TrySpawn();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.buildIndex == gameSceneIndex)
            TrySpawn();
    }

    public override void OnLeftRoom()
    {
        // Leaving while dead used to leave the respawn coroutine running, so it would come back
        // a few seconds later and try to spawn a player into a room we were no longer in.
        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }

        ClearDeathState();
        localController = null;
    }

    void TrySpawn()
    {
        if (spawnRoutine == null && localController == null && deathRoutine == null)
            spawnRoutine = StartCoroutine(SpawnWhenReady());
    }

    IEnumerator SpawnWhenReady()
    {
        // LoadLevel sets IsMessageQueueRunning false and PUN turns it back on from its own
        // sceneLoaded handler, which is registered after ours - so raising a spawn straight
        // out of OnSceneLoaded happened while we weren't sending anything.
        while (PhotonNetwork.InRoom == false
               || PhotonNetwork.IsMessageQueueRunning == false
               || SceneManager.GetActiveScene().buildIndex != gameSceneIndex
               || SpawnManager.Instance == null)
        {
            yield return null;
        }

        spawnRoutine = null;

        if (localController != null)
            yield break;

        Transform point = SpawnManager.Instance.GetSpawnpoint();
        Vector3 position = point != null ? point.position : Vector3.zero;
        Quaternion rotation = point != null ? point.rotation : Quaternion.identity;

        localController = PhotonNetwork.Instantiate(playerPrefab, position, rotation, 0);
    }

    /// <summary>
    /// Called by the local player when its health hits zero. Dying used to destroy and respawn
    /// the controller in the same frame, which meant death had no weight at all - you barely
    /// registered it had happened.
    /// </summary>
    public void HandleLocalDeath(Vector3 where, Vector3 facing)
    {
        if (deathRoutine == null)
            deathRoutine = StartCoroutine(DieThenRespawn(where, facing));
    }

    IEnumerator DieThenRespawn(Vector3 where, Vector3 facing)
    {
        AwaitingRespawn = true;
        RespawnAt = Time.time + MatchState.RespawnDelay;

        // Destroy first, then build the stand-in camera. The player's camera carries the only
        // AudioListener, and two live at once makes Unity complain and mute one of them.
        if (localController != null)
            PhotonNetwork.Destroy(localController);

        localController = null;

        spectatorCamera = BuildSpectatorCamera(where, facing);

        while (Time.time < RespawnAt)
            yield return null;

        ClearDeathState();
        deathRoutine = null;

        TrySpawn();
    }

    // Something has to keep rendering while the controller is gone, or death is a black screen.
    // Sits slightly above where you fell and looks down at it, which reads as a body cam.
    GameObject BuildSpectatorCamera(Vector3 where, Vector3 facing)
    {
        GameObject host = new GameObject("~DeathCamera");

        // Up and behind where you were looking, then pointed back at the spot. A random yaw
        // was cheaper and half the time showed you a wall.
        host.transform.position = where + Vector3.up * 3.5f - facing * 2.5f;
        host.transform.LookAt(where);

        Camera camera = host.AddComponent<Camera>();
        host.AddComponent<AudioListener>();

        // Nameplates find the camera through here, so they keep facing the right way while dead.
        PlayerController.SetLocalCamera(camera);

        return host;
    }

    void ClearDeathState()
    {
        AwaitingRespawn = false;

        if (spectatorCamera != null)
        {
            Destroy(spectatorCamera);
            spectatorCamera = null;
        }
    }


    // Stats live in custom properties, which Photon replicates and the scoreboard already
    // reads. MatchState owns writing them - it keeps the running tally, because a property does
    // not update the local cache until the server echoes it, so read-increment-write loses a
    // kill any time two land inside one round trip.
    public static int GetStat(Player player, string key)
    {
        if (player != null && player.CustomProperties.TryGetValue(key, out object value) && value is int i)
            return i;

        return 0;
    }

    // Belt and braces. Everything PUN does - sending, serializing, dispatching - is gated on this
    // one flag, so if it ever gets stuck off the client goes silently deaf while the game carries
    // on running normally. Which is exactly what the late-join bug looked like.
    //
    // Grace period rather than an instant flip, because LoadLevel turns it off legitimately for
    // a moment. Can't use LevelLoadingProgress to detect that - it sticks at 1 forever once any
    // scene has loaded, so it can't tell you whether a load is in progress.
    const float queueStuckGrace = 2f;
    float queueOffSince = -1f;

    void Update()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMessageQueueRunning)
        {
            queueOffSince = -1f;
            return;
        }

        if (queueOffSince < 0f)
        {
            queueOffSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - queueOffSince < queueStuckGrace)
            return;

        Debug.LogWarning($"Message queue stuck off for {queueStuckGrace}s while in a room. Turning it back on.");
        PhotonNetwork.IsMessageQueueRunning = true;
        queueOffSince = -1f;
    }
}
