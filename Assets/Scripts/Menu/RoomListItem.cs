using UnityEngine;
using Photon.Realtime;
using TMPro;

/// <summary>
/// One row of the room browser: the room's name, what it's playing and how full it is, as three
/// columns - the table the lobby browser is laid out as (see MenuBuilder). Clicking it joins.
/// </summary>
public class RoomListItem : MonoBehaviour
{
    [Tooltip("The room's name column. If only this is set, it gets the whole line on its own.")]
    [SerializeField] TMP_Text text;
    [SerializeField] TMP_Text modeText;
    [SerializeField] TMP_Text playersText;
    public RoomInfo info;

    public void SetUp(RoomInfo _info)
    {
        info = _info;

        // Published in CustomRoomPropertiesForLobby, so it's here without joining first.
        MatchMode mode = MatchMode.Deathmatch;
        if (info.CustomProperties != null
            && info.CustomProperties.TryGetValue(MatchState.ModeKey, out object value)
            && value is int m)
        {
            mode = (MatchMode)m;
        }

        // Cleaned on display too - another build's client, or anything that skipped the input
        // field, can publish any name at all. See PlayerNames.
        string name = PlayerNames.Clean(info.Name, PlayerNames.MaxRoomNameLength);
        string count = info.MaxPlayers > 0 ? $"{info.PlayerCount}/{info.MaxPlayers}" : info.PlayerCount.ToString();

        if (modeText == null && playersText == null)
        {
            if (text != null)
                text.text = $"{name}  [{MatchModes.Of(mode).ShortName}]  ({count})";
            return;
        }

        if (text != null)
            text.text = name.ToUpper();

        if (modeText != null)
            modeText.text = MatchModes.Of(mode).DisplayName;

        if (playersText != null)
            playersText.text = count;
    }

    public void OnClick()
    {
        // Launcher dies with the menu scene, so a click mid-transition finds nothing.
        if (Launcher.Instance == null)
            return;

        Launcher.Instance.JoinRoom(info);
    }
}
