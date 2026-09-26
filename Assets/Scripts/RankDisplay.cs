using UnityEngine;

/// <summary>
/// 1ST/2ND/3RD in gold/silver/bronze, everything past that a plain "NTH" in a neutral colour.
/// Pulled out of `GameHud.UpdateStandings` so the tab scoreboard can show the same medal ladder
/// instead of growing its own copy of these two arrays - the exact kind of duplication a full
/// codebase review just spent a pass finding and removing elsewhere.
/// </summary>
public static class RankDisplay
{
    // This game caps at 8 players, so nothing past 8TH can ever be asked for - no
    // 11th/21st-style exception needed.
    static readonly string[] Suffixes = { "1ST", "2ND", "3RD" };

    // Gold/silver/bronze - the same medal-ladder colours the crate rarities reuse for their own
    // top tier (CrateRarityInfo.ColorFor(Apex)), not picked independently of them.
    static readonly string[] ColourHexes = { "FFD11F", "C7CBD1", "C87F35" };
    const string NeutralHex = "8a8a90";

    public static string Suffix(int place) =>
        place < Suffixes.Length ? Suffixes[place] : $"{place + 1}TH";

    public static string ColourHex(int place) =>
        place < ColourHexes.Length ? ColourHexes[place] : NeutralHex;

    public static Color Colour(int place)
    {
        ColorUtility.TryParseHtmlString("#" + ColourHex(place), out Color colour);
        return colour;
    }
}
