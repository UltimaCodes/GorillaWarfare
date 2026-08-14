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
        // Launcher lives in the Menu scene and is destroyed on scene load; a stale button click
        // during the transition would otherwise dereference a null Instance.
        if (Launcher.Instance == null)
        {
            Debug.LogWarning("[RoomListItem] clicked with no Launcher present.", this);
            return;
        }

        Launcher.Instance.JoinRoom(info);
    }
}
