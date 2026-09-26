using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Photon.Realtime;
using Photon.Pun;

/// <summary>
/// The tab-held scoreboard. Rebuilt from scratch, the same "one pooled row per player" shape
/// GameHud.UpdateStandings already uses for the post-match screen, rather than the old
/// one-MonoBehaviour-per-player design (ScoreboardItem.cs, retired) - that shape made ranking,
/// grouping by team and inserting a header row all real work; this one gets them for the price of
/// building the rows in the right order, the same way the post-match screen already does.
///
/// Rows are a real table now (Rank/Name/Primary/Secondary/Streak, five fixed-width columns built
/// by ScoreboardBuilder.cs) rather than one rich-text line - reported directly against a CS2
/// reference, adapted to this game's own fields rather than copied: no money or MVP stars, but
/// the stat that actually decides the mode (style score or ladder rung), K/D, and a streak
/// flourish, styled in the HUD's own outline-and-underlay language instead of CS2's.
/// </summary>
public class Scoreboard : MonoBehaviourPunCallbacks
{
    [SerializeField] RectTransform container;
    [SerializeField] RectTransform backdrop;
    [SerializeField] RectTransform rowTemplate;
    [SerializeField] CanvasGroup canvasGroup;

    /// Read-only handles for a batch-mode test to pull real, laid-out world corners off of -
    /// there's no mouse or keyboard in batch mode to hold Tab with, and no camera render can see
    /// an overlay canvas anyway, so a numeric readback of the actual RectTransforms after a real
    /// layout pass is the only way left to check "is the text actually inside the backdrop."
    public RectTransform ContainerRect => container;
    public RectTransform BackdropRect => backdrop;

    /// Holds the scoreboard open on behalf of a test. Null means read the real key as normal -
    /// same shape as PlayerController.AimInputOverride, same reason: batch mode has no keyboard.
    public static bool? OpenOverride { get; set; }

    readonly List<RectTransform> rows = new List<RectTransform>();
    bool wasOpen;
    float nextRefresh;

    /// Best streak worth bragging about on a row - below this it's not a streak, it's just a
    /// couple of kills. First pass, unplayed, same as every other threshold in this project.
    const int StreakFlourishAt = 3;

    static readonly Color HeaderColour = new Color(0.62f, 0.62f, 0.68f);

    void Start() => Refresh();

    public override void OnPlayerEnteredRoom(Player newPlayer) => Refresh();
    public override void OnPlayerLeftRoom(Player otherPlayer) => Refresh();

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        if (wasOpen)
            Refresh();
    }

    void Update()
    {
        if (canvasGroup == null)
            return;

        // Not while the settings screen is up, where tab moves between fields and holding it
        // should not also throw the scoreboard over the panel.
        bool open = OpenOverride ?? (!SettingsMenu.IsOpen && KeyBinds.Held(KeyBinds.Action.Scoreboard));
        canvasGroup.alpha = open ? 1f : 0f;

        // Refreshed on open and every half second while held rather than every frame - numbers
        // ticking up while you glance at it is fine, a full re-sort every single frame for a
        // panel that's a quick glance most of the time is wasted work.
        if (open && (!wasOpen || Time.unscaledTime >= nextRefresh))
        {
            Refresh();
            nextRefresh = Time.unscaledTime + 0.5f;
        }

        wasOpen = open;
    }

    void Refresh()
    {
        if (container == null || rowTemplate == null)
            return;

        MatchModeInfo info = MatchModes.Of(MatchState.Mode);

        int shown = 0;
        Fill(Row(shown++), string.Empty, "NAME", info.ShowsLadder ? "LADDER" : "SCORE", "K / D", string.Empty,
             HeaderColour, FontStyles.Normal);

        shown = info.UsesTeams
            ? BuildTeam(0, BuildTeam(1, shown, info), info)
            : BuildFlat(PhotonNetwork.PlayerList, shown, info, false);

        for (int i = shown; i < rows.Count; i++)
            rows[i].gameObject.SetActive(false);
    }

    int BuildTeam(int team, int shown, MatchModeInfo info)
    {
        List<Player> members = new List<Player>();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (PlayerColours.TeamOf(player) == team)
                members.Add(player);
        }

        if (members.Count == 0)
            return shown;

        Fill(Row(shown++), string.Empty, PlayerColours.TeamNames[team],
             PlayerColours.TeamScore(team).ToString(), string.Empty, string.Empty,
             PlayerColours.TeamPalette[team], FontStyles.Bold);

        return BuildFlat(members, shown, info, true);
    }

    int BuildFlat(IList<Player> players, int shown, MatchModeInfo info, bool teamMode)
    {
        List<Player> ranked = new List<Player>(players);
        ranked.Sort((a, b) => RankStat(b, info).CompareTo(RankStat(a, info)));

        for (int i = 0; i < ranked.Count; i++)
        {
            Player person = ranked[i];

            // Team mode already sorts each side's own block - a medal on "2nd on your team of
            // four" reads as a real placement it isn't, so the rank prefix is FFA/gun-game only.
            string rank = teamMode ? string.Empty : $"<color=#{RankDisplay.ColourHex(i)}>{RankDisplay.Suffix(i)}</color>";

            (string primary, string secondary) = Stats(person, info);
            int bestStreak = RoomManager.GetStat(person, RoomManager.BestStreakKey);
            string streak = bestStreak >= StreakFlourishAt ? $"{bestStreak} STREAK" : string.Empty;

            // Every row tinted the colour that player actually renders in - the same association
            // the kill feed and the crosshair-picker swatches already use, team or not. The local
            // player's own row is bolded on top of that rather than recoloured, so it stands out
            // even sitting next to two teammates who share its exact colour.
            FontStyles style = person == PhotonNetwork.LocalPlayer ? FontStyles.Bold : FontStyles.Normal;
            Fill(Row(shown++), rank, MatchState.NameOf(person).ToUpper(), primary, secondary, streak,
                 PlayerColours.For(person), style);
        }

        return shown;
    }

    static (string primary, string secondary) Stats(Player person, MatchModeInfo info)
    {
        string kd = $"{RoomManager.GetStat(person, RoomManager.KillsKey)} / "
                   + $"{RoomManager.GetStat(person, RoomManager.DeathsKey)}";

        if (info.ShowsLadder)
        {
            string[] ladder = WeaponLoadout.GunGameLadder;
            int rung = Mathf.Clamp(MatchState.LadderRung(person), 0, ladder.Length - 1);
            string primary = $"{rung + 1}/{ladder.Length}  {WeaponLoadout.DisplayName(ladder[rung]).ToUpper()}";
            return (primary, kd);
        }

        if (info.RanksByStyleScore)
        {
            int score = RoomManager.GetStat(person, RoomManager.StyleScoreKey);
            return ($"{score}  {StyleScore.RankForScore(score)}", kd);
        }

        return (kd, string.Empty);
    }

    /// One number to sort by, whatever the mode actually decides itself by. Gun game breaks ties
    /// within a rung by kills earned on that rung - two people can share a rung a whole weapon
    /// apart in progress toward the next one.
    static int RankStat(Player player, MatchModeInfo info)
    {
        if (info.ShowsLadder)
        {
            return MatchState.LadderRung(player) * 1000
                 + RoomManager.GetStat(player, MatchState.RungKillsKey);
        }

        return RoomManager.GetStat(player,
            info.RanksByStyleScore ? RoomManager.StyleScoreKey : RoomManager.KillsKey);
    }

    static void Fill(RectTransform row, string rank, string name, string primary, string secondary,
                     string streak, Color colour, FontStyles style)
    {
        Set(row, "Rank", rank, colour, style);
        Set(row, "Name", name, colour, style);
        Set(row, "Primary", primary, colour, style);
        Set(row, "Secondary", secondary, colour, style);
        Set(row, "Streak", streak, colour, style);
    }

    static void Set(RectTransform row, string column, string value, Color colour, FontStyles style)
    {
        TMP_Text text = Part<TMP_Text>(row, column);

        if (text == null)
            return;

        text.text = value;
        text.color = colour;
        text.fontStyle = style;
    }

    static T Part<T>(RectTransform row, string childName) where T : Component
    {
        Transform child = row.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }

    RectTransform Row(int index)
    {
        while (rows.Count <= index)
            rows.Add(Instantiate(rowTemplate, container));

        RectTransform row = rows[index];
        row.gameObject.SetActive(true);
        return row;
    }
}
