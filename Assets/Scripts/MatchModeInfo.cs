using System.Collections.Generic;

/// <summary>
/// What each <see cref="MatchMode"/> actually is, in one place.
///
/// Found during a full-codebase review: display name, "does this mode use teams", "does it rank
/// by style score or by kills" and "does it show the gun-game ladder" were each re-derived from
/// the raw enum independently across GameHud, ColourPicker, ModeSelector, PlayerColours,
/// RoomListItem, ScoreboardItem, SingleShotGun and VineGrapple - eight files a new mode had to be
/// hand-added to, with no compiler error if one was missed. This is the "add one entry here"
/// version, for Party and whatever comes after it.
///
/// Deliberately doesn't own match length, loadout rules or win-condition selection - those stay
/// in <see cref="MatchState"/>, which is already the one place they live and which this project's
/// own docs mark as fragile enough not to restructure without a reason better than tidiness.
/// </summary>
public readonly struct MatchModeInfo
{
    public readonly string DisplayName;
    public readonly string ShortName;
    public readonly string Description;
    public readonly bool UsesTeams;
    public readonly bool RanksByStyleScore;
    public readonly bool ShowsLadder;

    public MatchModeInfo(string displayName, string shortName, string description, bool usesTeams,
                         bool ranksByStyleScore, bool showsLadder)
    {
        DisplayName = displayName;
        ShortName = shortName;
        Description = description;
        UsesTeams = usesTeams;
        RanksByStyleScore = ranksByStyleScore;
        ShowsLadder = showsLadder;
    }
}

public static class MatchModes
{
    static readonly Dictionary<MatchMode, MatchModeInfo> Info = new Dictionary<MatchMode, MatchModeInfo>
    {
        [MatchMode.Deathmatch] = new MatchModeInfo(
            "DEATHMATCH", "DM", "a random banana every life, most kills on the clock",
            usesTeams: false, ranksByStyleScore: true, showsLadder: false),

        [MatchMode.GunGame] = new MatchModeInfo(
            "GUN GAME", "GG", "climb the ladder, two kills a rung, win on the peel",
            usesTeams: false, ranksByStyleScore: false, showsLadder: true),

        [MatchMode.TeamDeathmatch] = new MatchModeInfo(
            "TEAM DEATHMATCH", "TDM", "red against blue, no friendly fire, most kills wins it",
            usesTeams: true, ranksByStyleScore: true, showsLadder: false),
    };

    public static MatchModeInfo Of(MatchMode mode) => Info[mode];
}
