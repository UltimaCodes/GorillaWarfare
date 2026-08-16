using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// Who is who, by colour.
///
/// Five identical dark gorillas in a dim room is a genuine readability problem - you cannot tell
/// whether the shape ahead of you is a person or a rock, let alone which person. A colour per
/// player fixes that, and once colours exist teams are almost free: a team is just a colour
/// everybody on it shares.
///
/// A fixed palette rather than a colour wheel, deliberately. The point of this is telling people
/// apart at forty metres in bad light, and a wheel lets two players pick shades of the same
/// green and undo the entire feature. Eight choices, all far apart, none of them the brown the
/// model already is.
/// </summary>
public static class PlayerColours
{
    /// The colour you picked in the lobby. A player property, so it follows you between matches
    /// and everybody sees the same answer.
    public const string ColourKey = "colour";

    /// Which team you are on, or -1. Assigned by the master, not chosen.
    public const string TeamKey = "team";

    /// <summary>
    /// Saturated and far apart. These get read at distance against a dark map, so anything
    /// subtle is wasted - and the game's whole look is loud anyway.
    /// </summary>
    public static readonly Color[] Palette =
    {
        new Color(1.00f, 0.85f, 0.10f),  // banana
        new Color(1.00f, 0.35f, 0.05f),  // orange
        new Color(0.95f, 0.15f, 0.35f),  // raspberry
        new Color(0.75f, 0.25f, 1.00f),  // grape
        new Color(0.20f, 0.55f, 1.00f),  // blueberry
        new Color(0.10f, 0.90f, 0.85f),  // mint
        new Color(0.45f, 1.00f, 0.15f),  // lime
        new Color(0.98f, 0.98f, 0.98f),  // albino
    };

    public static readonly string[] Names =
    {
        "banana", "orange", "raspberry", "grape", "blueberry", "mint", "lime", "albino",
    };

    /// <summary>
    /// Team colours, which override the personal one entirely while a team mode is running.
    ///
    /// Telling your team from theirs matters more than telling your teammates from each other -
    /// if you have to think about it for even a moment you have already shot them.
    /// </summary>
    public static readonly Color[] TeamPalette =
    {
        new Color(1.00f, 0.20f, 0.18f),  // red
        new Color(0.20f, 0.50f, 1.00f),  // blue
    };

    public static readonly string[] TeamNames = { "RED", "BLUE" };

    public static int IndexOf(Player player)
    {
        int index = RoomManager.GetStat(player, ColourKey);
        return Mathf.Clamp(index, 0, Palette.Length - 1);
    }

    /// Which team, or -1 for none. Always -1 outside a team mode, so nothing has to remember to
    /// clear it when the mode changes.
    public static int TeamOf(Player player)
    {
        if (MatchState.Mode != MatchMode.TeamDeathmatch || player == null)
            return -1;

        if (!player.CustomProperties.TryGetValue(TeamKey, out object value) || !(value is int team))
            return -1;

        return team >= 0 && team < TeamPalette.Length ? team : -1;
    }

    /// The colour this player is actually drawn in right now.
    public static Color For(Player player)
    {
        int team = TeamOf(player);
        return team >= 0 ? TeamPalette[team] : Palette[IndexOf(player)];
    }

    /// <summary>
    /// Whether two players are on the same side.
    ///
    /// False outside a team mode, including for a player compared with themselves - callers use
    /// this to decide whether to block damage, and blocking your own damage would switch off
    /// fall damage and the void kill.
    /// </summary>
    public static bool SameTeam(Player a, Player b)
    {
        if (a == null || b == null || MatchState.Mode != MatchMode.TeamDeathmatch)
            return false;

        int left = TeamOf(a);
        return left >= 0 && left == TeamOf(b);
    }

    /// Picks a colour for the local player. Anyone may call this; it is a preference, not a rule.
    public static void Choose(int index)
    {
        if (!PhotonNetwork.InRoom)
            return;

        int clamped = ((index % Palette.Length) + Palette.Length) % Palette.Length;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ColourKey, clamped } });
    }

    /// <summary>
    /// Whether somebody else already has this colour.
    ///
    /// Not enforced - two people insisting on lime is their business and the game still works -
    /// but the lobby greys the swatch so it takes a deliberate act rather than an accident.
    /// </summary>
    public static bool Taken(int index)
    {
        if (!PhotonNetwork.InRoom)
            return false;

        foreach (Player other in PhotonNetwork.PlayerList)
        {
            if (other != PhotonNetwork.LocalPlayer && IndexOf(other) == index)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Splits the room into two sides, as evenly as it can.
    ///
    /// Master only, and recomputed from scratch rather than incrementally: assigning each
    /// arrival to the smaller side drifts badly once people start leaving, and a room that ends
    /// up four against one is a room nobody wants to be in the one.
    ///
    /// Sorted by actor number so every client would compute the same answer, which makes it
    /// checkable and makes a host migration mid-assignment harmless.
    /// </summary>
    public static void AssignTeams()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
            return;

        Player[] players = PhotonNetwork.PlayerList;
        System.Array.Sort(players, (a, b) => a.ActorNumber.CompareTo(b.ActorNumber));

        for (int i = 0; i < players.Length; i++)
        {
            int team = MatchState.Mode == MatchMode.TeamDeathmatch ? i % TeamPalette.Length : -1;

            if (TeamOf(players[i]) == team && team >= 0)
                continue;

            players[i].SetCustomProperties(new Hashtable { { TeamKey, team } });
        }
    }

    /// <summary>
    /// How many kills each side has, for the scoreboard and for deciding who won.
    ///
    /// Summed from player kills rather than kept as its own counter, because a team score that
    /// is maintained separately can disagree with the players it is made of - and when it does,
    /// the scoreboard and the winner disagree with each other in front of everybody.
    /// </summary>
    public static int TeamScore(int team)
    {
        if (!PhotonNetwork.InRoom)
            return 0;

        int total = 0;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            if (TeamOf(player) == team)
                total += RoomManager.GetStat(player, RoomManager.KillsKey);
        }

        return total;
    }
}
