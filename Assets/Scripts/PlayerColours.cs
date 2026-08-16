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
    /// Puts one player on one side.
    ///
    /// Anyone may move themselves; only the host may move anybody else. PUN does not enforce
    /// that - any client can write any player's properties - so it is enforced here, which is
    /// the same amount of security everything else in this game has and the right amount for
    /// five friends.
    /// </summary>
    public static bool SetTeam(Player player, int team)
    {
        if (player == null || !PhotonNetwork.InRoom || MatchState.Mode != MatchMode.TeamDeathmatch)
            return false;

        if (player != PhotonNetwork.LocalPlayer && !PhotonNetwork.IsMasterClient)
            return false;

        int clamped = Mathf.Clamp(team, 0, TeamPalette.Length - 1);

        if (TeamOf(player) == clamped)
            return false;

        player.SetCustomProperties(new Hashtable { { TeamKey, clamped } });
        return true;
    }

    /// Whether the local player is allowed to move this one.
    public static bool CanAssign(Player player) =>
        MatchState.Mode == MatchMode.TeamDeathmatch
        && (player == PhotonNetwork.LocalPlayer || PhotonNetwork.IsMasterClient);

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

        // Outside a team mode nobody has a side. Clearing rather than leaving stale values means
        // switching modes back and forth cannot leave somebody wearing red in a deathmatch.
        if (MatchState.Mode != MatchMode.TeamDeathmatch)
        {
            foreach (Player player in players)
            {
                if (TeamOf(player) >= 0 || player.CustomProperties.ContainsKey(TeamKey))
                    player.SetCustomProperties(new Hashtable { { TeamKey, -1 } });
            }

            return;
        }

        // People pick their own sides now, so this fills gaps rather than dealing the room out
        // from scratch. Reassigning everybody on every join would silently undo a choice
        // somebody made ten seconds earlier, which is worse than a slightly uneven match.
        int[] size = new int[TeamPalette.Length];

        foreach (Player player in players)
        {
            int team = TeamOf(player);

            if (team >= 0)
                size[team]++;
        }

        foreach (Player player in players)
        {
            if (TeamOf(player) >= 0)
                continue;

            int smallest = size[0] <= size[1] ? 0 : 1;
            player.SetCustomProperties(new Hashtable { { TeamKey, smallest } });
            size[smallest]++;
        }

        // Only step in when it has gone properly lopsided. One more on a side is a match; three
        // against one is nobody's idea of an evening, and at that point being moved is a
        // kindness rather than an interference.
        while (Mathf.Abs(size[0] - size[1]) > 1)
        {
            int from = size[0] > size[1] ? 0 : 1;
            int to = 1 - from;
            Player moved = null;

            // The most recent arrival on the crowded side, so the person who has been there
            // longest is not the one uprooted.
            foreach (Player player in players)
            {
                if (TeamOf(player) == from)
                    moved = player;
            }

            if (moved == null)
                break;

            moved.SetCustomProperties(new Hashtable { { TeamKey, to } });
            size[from]--;
            size[to]++;
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
