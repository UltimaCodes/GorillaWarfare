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

        // Adding twice used to orphan the first row - it stayed on screen forever.
        if (scoreboardItems.ContainsKey(player))
            return;

        ScoreboardItem item = Instantiate(scoreboardItemPrefab, container).GetComponent<ScoreboardItem>();
        if (item == null)
            return;

        item.Initialize(player);
        scoreboardItems[player] = item;
    }

    void RemoveScoreboardItem(Player player)
    {
        // Was indexing straight into the dictionary, which threw for anyone who left
        // without ever being added.
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

        // Read every frame rather than on the key events, because rebinding the scoreboard
        // while holding the old key would otherwise leave it stuck open forever.
        canvasGroup.alpha = KeyBinds.Held(KeyBinds.Action.Scoreboard) ? 1f : 0f;
    }
}
