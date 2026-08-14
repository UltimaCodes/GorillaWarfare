using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using TMPro;

public class PlayerListItem : MonoBehaviourPunCallbacks
{
    [SerializeField] TMP_Text text;

    Player player;

    public void SetUp(Player _player)
    {
        player = _player;

        if (text == null || player == null)
            return;

        string nick = player.NickName;
        text.text = string.IsNullOrWhiteSpace(nick) ? $"Player {player.ActorNumber}" : nick;
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (player != null && player == otherPlayer)
            Destroy(gameObject);
    }

    public override void OnLeftRoom()
    {
        Destroy(gameObject);
    }
}
