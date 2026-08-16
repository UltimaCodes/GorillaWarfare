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
            return;

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

        // Fall back to "0" - before your first kill the property doesn't exist yet and
        // the row just showed whatever placeholder text was on the prefab.
        if (killsText != null)
            killsText.text = player.CustomProperties.TryGetValue(RoomManager.KillsKey, out object kills) ? kills.ToString() : "0";

        if (deathsText != null)
            deathsText.text = player.CustomProperties.TryGetValue(RoomManager.DeathsKey, out object deaths) ? deaths.ToString() : "0";

        // In gun game the rung is the score - kills only matter as the thing that moves it, and
        // two people on the same kill count can be a whole weapon apart. Shown on the name so it
        // needs no new column, and named rather than numbered because "BIG MIKE" tells you what
        // they are holding and "4" does not.
        if (usernameText != null && MatchState.Mode == MatchMode.GunGame)
        {
            string[] ladder = WeaponLoadout.GunGameLadder;
            int rung = Mathf.Clamp(MatchState.LadderRung(player), 0, ladder.Length - 1);

            string nick = player.NickName;
            string name = string.IsNullOrWhiteSpace(nick) ? $"Player {player.ActorNumber}" : nick;

            usernameText.text = $"{name}   {rung + 1}/{ladder.Length} "
                                + WeaponLoadout.DisplayName(ladder[rung]).ToUpper();
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (player == null || targetPlayer != player)
            return;

        if (changedProps.ContainsKey(RoomManager.KillsKey)
            || changedProps.ContainsKey(RoomManager.DeathsKey)
            || changedProps.ContainsKey(MatchState.RungKey))
            UpdateStats();
    }
}
