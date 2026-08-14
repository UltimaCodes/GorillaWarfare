using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using System.Linq;
using System.IO;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class PlayerManager : MonoBehaviour
{
    PhotonView PV;

    GameObject controller;

    int kills;
    int deaths;

    void Awake()
    {
        PV = GetComponent<PhotonView>();
    }

    void Start()
    {
        if (PV.IsMine)
        {
            CreateController();
        }
    }

    // Forward slash, not Path.Combine: this string is a Resources key sent over the network,
    // and Path.Combine produces a backslash on Windows.
    const string playerControllerPrefab = "PhotonPrefabs/PlayerController";

    void CreateController()
    {
        if (SpawnManager.Instance == null)
        {
            Debug.LogError("[Spawn] no SpawnManager in scene; cannot create controller.", this);
            return;
        }

        Transform spawnpoint = SpawnManager.Instance.GetSpawnpoint();
        if (spawnpoint == null)
        {
            Debug.LogError("[Spawn] SpawnManager returned no spawnpoint; cannot create controller.", this);
            return;
        }

        controller = PhotonNetwork.Instantiate(playerControllerPrefab, spawnpoint.position, spawnpoint.rotation, 0, new object[] { PV.ViewID });
    }

    public void Die()
    {
        PhotonNetwork.Destroy(controller);
        CreateController();

        deaths++;

        Hashtable hash = new Hashtable();
        hash.Add("deaths", deaths);
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    public void GetKill()
    {
        PV.RPC(nameof(RPC_GetKill), PV.Owner);
    }

    [PunRPC]
    void RPC_GetKill()
    {
        kills++;

        Hashtable hash = new Hashtable();
        hash.Add("kills", kills);
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    public static PlayerManager Find(Player player)
    {
        // FirstOrDefault, not SingleOrDefault: Single throws if a duplicate ever exists, which
        // would turn a cosmetic desync into an exception. FindObjectsByType replaces the
        // FindObjectsOfType deprecated in Unity 6.
        return FindObjectsByType<PlayerManager>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.PV != null && x.PV.Owner == player);
    }
}