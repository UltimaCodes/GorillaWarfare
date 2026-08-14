using System.Collections.Generic;
using UnityEngine;
using Photon.Realtime;
using Photon.Pun;

public class Scoreboard : MonoBehaviourPunCallbacks
{
    [SerializeField] Transform container;
    [SerializeField] GameObject scoreboardItemPrefab;
    [SerializeField] CanvasGroup canvasGroup;

    readonly Dictionary<Player, ScoreboardItem> scoreboardItems = new Dictionary<Player, ScoreboardItem>();

    void Start()
    {
        foreach (Player player in PhotonNetwork.PlayerList)
            AddScoreboardItem(player);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        AddScoreboardItem(newPlayer);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        RemoveScoreboardItem(otherPlayer);
    }

    void AddScoreboardItem(Player player)
    {
        if (player == null || container == null || scoreboardItemPrefab == null)
            return;

        // Adding a player who already has a row used to overwrite the dictionary entry and
        // orphan the old GameObject, leaking a row that could never be removed again.
        if (scoreboardItems.ContainsKey(player))
            return;

        ScoreboardItem item = Instantiate(scoreboardItemPrefab, container).GetComponent<ScoreboardItem>();
        if (item == null)
        {
            Debug.LogError("[Scoreboard] item prefab has no ScoreboardItem component.", this);
            return;
        }

        item.Initialize(player);
        scoreboardItems[player] = item;
    }

    void RemoveScoreboardItem(Player player)
    {
        // Previously indexed the dictionary directly, throwing KeyNotFoundException whenever a
        // player left who had never been added -- which happens for anyone who joins and leaves
        // while this scoreboard is mid-initialisation.
        if (player == null || !scoreboardItems.TryGetValue(player, out ScoreboardItem item))
            return;

        if (item != null)
            Destroy(item.gameObject);

        scoreboardItems.Remove(player);
    }

    void Update()
    {
        if (canvasGroup == null)
            return;

        if (Input.GetKeyDown(KeyCode.Tab))
            canvasGroup.alpha = 1;
        else if (Input.GetKeyUp(KeyCode.Tab))
            canvasGroup.alpha = 0;
    }
}
