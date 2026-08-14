using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class PlayerManager : MonoBehaviour
{
    const string playerControllerPrefab = "PhotonPrefabs/PlayerController";

    // Find() runs on every kill and used to scan the whole scene each time.
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
            Debug.LogError("No SpawnManager in the scene.", this);
            return;
        }

        Transform spawnpoint = SpawnManager.Instance.GetSpawnpoint();
        if (spawnpoint == null)
        {
            Debug.LogError("SpawnManager gave us no spawnpoint.", this);
            return;
        }

        controller = PhotonNetwork.Instantiate(playerControllerPrefab, spawnpoint.position, spawnpoint.rotation, 0, new object[] { PV.ViewID });
    }

    public void Die()
    {
        // If CreateController bailed earlier this is null, and destroying null left you
        // dead for good.
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

        // Fallback for the window before Owner resolves. First, not Single - Single throws
        // if there's ever a duplicate.
        PlayerManager found = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None)
            .FirstOrDefault(x => x.PV != null && x.PV.Owner == player);

        if (found != null)
            registry[player] = found;

        return found;
    }
}
