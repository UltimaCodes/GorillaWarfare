using TMPro;
using Photon.Realtime;
using Photon.Pun;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class ScoreboardItem : MonoBehaviourPunCallbacks
{
    public TMP_Text usernameText;
    public TMP_Text killsText;
    public TMP_Text deathsText;

    Player player;

    public void Initialize(Player player)
    {
        this.player = player;

        if (player == null)
        {
            Debug.LogError("[ScoreboardItem] initialised with a null player.", this);
            return;
        }

        if (usernameText != null)
        {
            string nick = player.NickName;
            usernameText.text = string.IsNullOrWhiteSpace(nick) ? $"Player {player.ActorNumber}" : nick;
        }

        UpdateStats();
    }

    void UpdateStats()
    {
        if (player == null)
            return;

        // Default to "0" rather than leaving whatever the prefab shipped with: a player who has
        // not yet scored has no custom property at all, so these rows previously displayed the
        // prefab's placeholder text until their first kill or death.
        if (killsText != null)
            killsText.text = player.CustomProperties.TryGetValue("kills", out object kills) ? kills.ToString() : "0";

        if (deathsText != null)
            deathsText.text = player.CustomProperties.TryGetValue("deaths", out object deaths) ? deaths.ToString() : "0";
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (player == null || targetPlayer != player)
            return;

        if (changedProps.ContainsKey("kills") || changedProps.ContainsKey("deaths"))
            UpdateStats();
    }
}
