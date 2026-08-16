using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// One row of the lobby list.
///
/// It used to print a name. It now says three things a lobby actually needs to answer: which
/// side you are on, who is running the room, and whether you can do anything about either.
///
/// Sides are shown by colouring the name rather than with a separate label or a swatch. It is
/// the one piece of text already on the row, everybody is already reading it, and a red name
/// next to a blue one needs no explaining.
/// </summary>
public class PlayerListItem : MonoBehaviourPunCallbacks
{
    [SerializeField] TMP_Text text;
    [SerializeField] Button button;

    Player player;

    /// <summary>
    /// What marks the host.
    ///
    /// A crown if the font has one and the word HOST if it does not. TextMeshPro draws a missing
    /// glyph as a hollow box, and a row reading "[] Ryaan" is worse than no marker at all -
    /// which is a real risk here, because these are display fonts Ryaan picked for looks rather
    /// than for coverage.
    /// </summary>
    string HostMark()
    {
        const char crown = '♛';

        if (text != null && text.font != null && text.font.HasCharacter(crown))
            return crown + "  ";

        return "Host  ";
    }

    public void SetUp(Player _player)
    {
        player = _player;

        if (button != null)
            button.onClick.AddListener(Clicked);

        Draw();
    }

    void Draw()
    {
        if (text == null || player == null)
            return;

        string nick = player.NickName;
        string name = string.IsNullOrWhiteSpace(nick) ? $"Player {player.ActorNumber}" : nick;

        text.text = (player.IsMasterClient ? HostMark() : string.Empty) + name;

        // In a team mode the side is the colour. Outside one it falls back to whatever colour
        // they picked for themselves, so the row still tells you which gorilla is which.
        int team = PlayerColours.TeamOf(player);
        text.color = team >= 0 ? PlayerColours.TeamPalette[team] : PlayerColours.For(player);

        // Only rows you can act on respond to a click. A button that highlights and then does
        // nothing is a worse answer than a button that does not highlight.
        if (button != null)
            button.interactable = PlayerColours.CanAssign(player);
    }

    /// <summary>
    /// Clicking a name moves that player to the other side.
    ///
    /// Your own always. Anybody else's only if you are the host - PlayerColours enforces that
    /// rather than trusting this, since a row is a piece of UI and the rule is a rule.
    /// </summary>
    void Clicked()
    {
        if (player == null || !PlayerColours.CanAssign(player))
            return;

        int team = PlayerColours.TeamOf(player);

        if (PlayerColours.SetTeam(player, team == 0 ? 1 : 0))
            GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);
    }

    // Any of these can change what this row should say, and all of them happen to somebody else.
    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) => Draw();
    public override void OnRoomPropertiesUpdate(Hashtable changed) => Draw();
    public override void OnMasterClientSwitched(Player newMasterClient) => Draw();

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        if (player != null && player == otherPlayer)
            Destroy(gameObject);
        else
            Draw();
    }

    public override void OnLeftRoom()
    {
        Destroy(gameObject);
    }
}
