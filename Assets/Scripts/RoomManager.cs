using System.Collections;
using UnityEngine;
using Photon.Pun;
using UnityEngine.SceneManagement;

public class RoomManager : MonoBehaviourPunCallbacks
{
    public static RoomManager instance;

    // Not Path.Combine - that gives a backslash on Windows, and this string goes over
    // the network as a Resources key.
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

        // Destroy is deferred, so the duplicate we just killed in Awake still runs OnEnable
        // and subscribes. That meant two PlayerManagers each after going back to the menu.
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
        if (scene.buildIndex != 1 || spawnPending)
            return;

        spawnPending = true;
        StartCoroutine(SpawnPlayerManagerWhenReady());
    }

    IEnumerator SpawnPlayerManagerWhenReady()
    {
        // LoadLevel turns the message queue off, and PUN only turns it back on from its own
        // sceneLoaded handler - which is registered after ours, so it hasn't run yet. Spawning
        // here meant raising the event while we weren't sending or dispatching anything.
        while (!PhotonNetwork.InRoom || !PhotonNetwork.IsMessageQueueRunning)
            yield return null;

        spawnPending = false;
        PhotonNetwork.Instantiate(playerManagerPrefab, Vector3.zero, Quaternion.identity);
    }
}
