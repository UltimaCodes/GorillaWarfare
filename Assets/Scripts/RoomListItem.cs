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

        if (text != null)
            text.text = $"{info.Name}  ({info.PlayerCount}{(info.MaxPlayers > 0 ? "/" + info.MaxPlayers : "")})";
    }

    public void OnClick()
    {
        // Launcher dies with the menu scene, so a click mid-transition finds nothing.
        if (Launcher.Instance == null)
            return;

        Launcher.Instance.JoinRoom(info);
    }
}
