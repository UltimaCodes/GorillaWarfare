using UnityEngine;
using Photon.Realtime;
using TMPro;

public class RoomListItem : MonoBehaviour
{
    [SerializeField] TMP_Text text;
    public RoomInfo info;

    public void SetUp(RoomInfo _info)
    {
        info = _info;

        if (text == null)
            return;

        // Published in CustomRoomPropertiesForLobby, so it's here without joining first.
        string mode = "DM";
        if (info.CustomProperties != null
            && info.CustomProperties.TryGetValue(MatchState.ModeKey, out object value)
            && value is int m)
        {
            mode = (MatchMode)m == MatchMode.GunGame ? "GUN GAME" : "DM";
        }

        string count = info.MaxPlayers > 0 ? $"{info.PlayerCount}/{info.MaxPlayers}" : info.PlayerCount.ToString();
        text.text = $"{info.Name}  [{mode}]  ({count})";
    }

    public void OnClick()
    {
        // Launcher dies with the menu scene, so a click mid-transition finds nothing.
        if (Launcher.Instance == null)
            return;

        Launcher.Instance.JoinRoom(info);
    }
}
