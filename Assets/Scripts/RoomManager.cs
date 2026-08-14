using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;

public class RoomManager : MonoBehaviourPunCallbacks
{
    public static RoomManager instance;

    // Unity's Resources API wants forward slashes, and this string is sent over the network as
    // the prefab key. Path.Combine yields a backslash on Windows, which happens to resolve there
    // but would not survive a mixed-platform room.
    const string playerManagerPrefab = "PhotonPrefabs/PlayerManager";

    bool spawnPending;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
        instance = this;
    }

    public override void OnEnable()
    {
        base.OnEnable();

        // A duplicate created by returning to the Menu scene is marked for destruction in Awake,
        // but Destroy is deferred to end of frame, so OnEnable still runs on it. Without this
        // guard the duplicate also subscribes, and every player spawns two PlayerManagers.
        if (instance != this)
            return;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    public override void OnDisable()
    {
        base.OnDisable();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (scene.buildIndex != 1)
            return;

        if (spawnPending)
            return;

        spawnPending = true;
        StartCoroutine(SpawnPlayerManagerWhenReady());
    }

    // PhotonNetwork.LoadLevel sets IsMessageQueueRunning = false, and PUN only restores it from
    // PhotonHandler's own sceneLoaded handler -- which is registered in PhotonHandler.Start(),
    // i.e. AFTER this component subscribed during the Menu scene's OnEnable. Unity invokes
    // sceneLoaded handlers in subscription order, so this method previously ran while the client
    // was neither sending nor dispatching, and raised the spawn event into a paused queue.
    //
    // Waiting for the queue to actually be running costs a frame or two and removes that whole
    // class of ordering problem.
    IEnumerator SpawnPlayerManagerWhenReady()
    {
        while (!PhotonNetwork.InRoom || !PhotonNetwork.IsMessageQueueRunning)
            yield return null;

        spawnPending = false;
        PhotonNetwork.Instantiate(playerManagerPrefab, Vector3.zero, Quaternion.identity);
    }
}
