using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class PlayerManager : MonoBehaviour
{
    // Forward slash, not Path.Combine: this string is a Resources key sent over the network,
    // and Path.Combine produces a backslash on Windows.
    const string playerControllerPrefab = "PhotonPrefabs/PlayerController";

    // Find() previously scanned every object in the scene on each call, and it is called on
    // every kill. Owners are stable for the lifetime of the object, so a registry answers in
    // constant time; the scan is kept only as a fallback for the window before Owner resolves.
    static readonly Dictionary<Player, PlayerManager> registry = new Dictionary<Player, PlayerManager>();

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
        Register();

        if (PV.IsMine)
            CreateController();
    }

    void Register()
    {
        if (PV != null && PV.Owner != null)
            registry[PV.Owner] = this;
    }

    void OnDestroy()
    {
        if (PV != null && PV.Owner != null && registry.TryGetValue(PV.Owner, out PlayerManager existing) && existing == this)
            registry.Remove(PV.Owner);
    }

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
        // Guarded: dying without a controller -- which happens if CreateController bailed out --
        // used to throw inside PhotonNetwork.Destroy and leave the player permanently dead.
        if (controller != null)
            PhotonNetwork.Destroy(controller);

        controller = null;
        CreateController();

        deaths++;

        Hashtable hash = new Hashtable { { "deaths", deaths } };
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

        Hashtable hash = new Hashtable { { "kills", kills } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
    }

    public static PlayerManager Find(Player player)
    {
        if (player == null)
            return null;

        if (registry.TryGetValue(player, out PlayerManager cached) && cached != null)
            return cached;

        // FirstOrDefault, not SingleOrDefault: Single throws if a duplicate ever exists, which
        // would turn a cosmetic desync into an exception. FindObjectsByType replaces the
        // FindObjectsOfType deprecated in Unity 6.
        PlayerManager found = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.PV != null && x.PV.Owner == player);

        if (found != null)
            registry[player] = found;

        return found;
    }
}
