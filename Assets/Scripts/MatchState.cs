using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum MatchMode
{
    Deathmatch = 0,
    GunGame = 1,
}

public enum MatchPhase
{
    Warmup = 0,
    Live = 1,
    Over = 2,
}

/// <summary>
/// Runs the match: which mode, which phase, how long is left, who won.
///
/// Every piece of that lives in room custom properties rather than in fields here. The master
/// client is the only one that writes them, everyone else reads - which means a client that
/// joins halfway through gets the whole match state handed to it by the server on join, with no
/// catch-up RPC to write and nothing to go stale. It also means the match survives the host
/// leaving: the new master picks up from the properties it already had.
///
/// The clock is a deadline, not a countdown. PhotonNetwork.Time is a server synchronised double,
/// so storing the moment the round ends lets every client work out the remaining time on its own
/// without anyone broadcasting a tick. A client that hitches for a second doesn't drift.
/// </summary>
public class MatchState : MonoBehaviourPunCallbacks
{
    public const string ModeKey = "mode";
    public const string PhaseKey = "phase";
    public const string EndsAtKey = "endsAt";
    public const string WinnerKey = "winner";

    /// Gun game position, per player.
    public const string RungKey = "rung";
    public const string RungKillsKey = "rungKills";

    [Header("Timings")]
    [SerializeField] float warmupSeconds = 8f;
    [SerializeField] float deathmatchSeconds = 300f;
    [SerializeField] float gunGameSeconds = 600f;
    [SerializeField] float scoreboardSeconds = 12f;
    [SerializeField] float respawnSeconds = 3f;

    [Header("Rules")]
    [SerializeField] int deathmatchWeaponCount = 3;
    [SerializeField] int killsPerRung = 2;

    public static MatchState Instance { get; private set; }

    /// One entry per kill, newest last. The HUD draws it; nothing else should mutate it.
    public static readonly List<KillEvent> Feed = new List<KillEvent>();

    public struct KillEvent
    {
        public string killer;
        public string victim;
        public string weapon;
        public bool headshot;
        public float at;
    }

    // The master's own tally. Custom properties do not update locally until the server echoes
    // them back, so incrementing straight off the replicated value loses a kill whenever two
    // land inside one round trip. Only the master writes scores, so its cache is the truth.
    readonly Dictionary<int, int> kills = new Dictionary<int, int>();
    readonly Dictionary<int, int> deaths = new Dictionary<int, int>();
    readonly Dictionary<int, int> rungKills = new Dictionary<int, int>();
    readonly Dictionary<int, int> rungs = new Dictionary<int, int>();

    public static float RespawnDelay => Instance != null ? Instance.respawnSeconds : 3f;

    public static MatchMode Mode => (MatchMode)RoomInt(ModeKey, (int)MatchMode.Deathmatch);
    public static MatchPhase Phase => (MatchPhase)RoomInt(PhaseKey, (int)MatchPhase.Warmup);

    /// Seconds left in the current phase, clamped at zero. Same answer on every client.
    public static float TimeLeft
    {
        get
        {
            if (!PhotonNetwork.InRoom)
                return 0f;

            if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(EndsAtKey, out object value)
                && value is double endsAt)
            {
                return Mathf.Max(0f, (float)(endsAt - PhotonNetwork.Time));
            }

            return 0f;
        }
    }

    public static Player Winner
    {
        get
        {
            int actor = RoomInt(WinnerKey, -1);
            return actor >= 0 && PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
        }
    }

    /// True while players should be able to move and shoot.
    public static bool InPlay => Phase == MatchPhase.Live;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void OnJoinedRoom()
    {
        Feed.Clear();

        // The room is fresh and nobody has set a phase yet, so start one.
        if (PhotonNetwork.IsMasterClient && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PhaseKey))
            BeginWarmup();
    }

    public override void OnLeftRoom()
    {
        Feed.Clear();
        kills.Clear();
        deaths.Clear();
        rungKills.Clear();
        rungs.Clear();
    }

    // The old master left mid-match. Everything needed to carry on is already in the room
    // properties; all the new master has to do is reload its own tally from them.
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        kills.Clear();
        deaths.Clear();
        rungKills.Clear();
        rungs.Clear();

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            kills[player.ActorNumber] = RoomManager.GetStat(player, RoomManager.KillsKey);
            deaths[player.ActorNumber] = RoomManager.GetStat(player, RoomManager.DeathsKey);
            rungKills[player.ActorNumber] = RoomManager.GetStat(player, RungKillsKey);
            rungs[player.ActorNumber] = RoomManager.GetStat(player, RungKey);
        }

        Debug.Log($"[match] took over as master mid {Phase}, restored {kills.Count} scores.");
    }

    // Anyone arriving mid-match needs a loadout, and in deathmatch that is the set this match
    // rolled - not whatever they would have picked for themselves.
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient && Phase != MatchPhase.Over)
            GiveLoadout(newPlayer);
    }

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
            return;

        if (TimeLeft > 0f)
            return;

        switch (Phase)
        {
            case MatchPhase.Warmup:
                BeginLive();
                break;

            case MatchPhase.Live:
                FinishMatch(LeaderByKills());
                break;

            case MatchPhase.Over:
                BeginWarmup();
                break;
        }
    }

    // ---- phase transitions, master only ----

    void BeginWarmup()
    {
        kills.Clear();
        deaths.Clear();
        rungKills.Clear();
        rungs.Clear();

        // Scores are per match, and PUN never clears player properties by itself - they even
        // follow you into the next room you join. Left alone, everyone starts a new match
        // carrying the last one's kills.
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            player.SetCustomProperties(new Hashtable
            {
                { RoomManager.KillsKey, 0 },
                { RoomManager.DeathsKey, 0 },
                { RungKey, 0 },
                { RungKillsKey, 0 },
            });
        }

        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Warmup },
            { WinnerKey, -1 },
            { EndsAtKey, PhotonNetwork.Time + warmupSeconds },
        });

        // Rolled once for the whole match so everyone fights with the same three.
        if (Mode == MatchMode.Deathmatch)
            rolledWeapons = WeaponLoadout.RandomSelection(deathmatchWeaponCount);

        foreach (Player player in PhotonNetwork.PlayerList)
            GiveLoadout(player);
    }

    void BeginLive()
    {
        float length = Mode == MatchMode.GunGame ? gunGameSeconds : deathmatchSeconds;

        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Live },
            { EndsAtKey, PhotonNetwork.Time + length },
        });
    }

    void FinishMatch(Player winner)
    {
        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Over },
            { WinnerKey, winner != null ? winner.ActorNumber : -1 },
            { EndsAtKey, PhotonNetwork.Time + scoreboardSeconds },
        });
    }

    string[] rolledWeapons;

    /// What a player should be carrying right now, under the current mode.
    void GiveLoadout(Player player)
    {
        string[] weapons = Mode == MatchMode.GunGame
            ? Rules.LoadoutForRung(RungOf(player), WeaponLoadout.GunGameLadder)
            : rolledWeapons ?? WeaponLoadout.RandomSelection(deathmatchWeaponCount);

        player.SetCustomProperties(
            new Hashtable { { PlayerController.LoadoutKey, Rules.Serialise(weapons) } });
    }

    // ---- kills ----

    /// <summary>
    /// Called on every client from the victim's death RPC. Only the master acts on it; everyone
    /// runs the feed side so the message appears at the same moment for all of them.
    /// </summary>
    public static void ReportKill(Player killer, Player victim, string weapon, bool headshot)
    {
        Feed.Add(new KillEvent
        {
            killer = killer != null ? NameOf(killer) : string.Empty,
            victim = NameOf(victim),
            weapon = weapon,
            headshot = headshot,
            at = Time.time,
        });

        if (Feed.Count > 32)
            Feed.RemoveRange(0, Feed.Count - 32);

        if (Instance != null && PhotonNetwork.IsMasterClient)
            Instance.ScoreKill(killer, victim, weapon);
    }

    void ScoreKill(Player killer, Player victim, string weapon)
    {
        if (Phase != MatchPhase.Live)
            return;

        if (victim != null)
        {
            deaths[victim.ActorNumber] = Bump(deaths, victim.ActorNumber);
            victim.SetCustomProperties(new Hashtable { { RoomManager.DeathsKey, deaths[victim.ActorNumber] } });
        }

        // Falling off the map, or shooting yourself, is a death and nothing else.
        if (killer == null || killer == victim)
            return;

        kills[killer.ActorNumber] = Bump(kills, killer.ActorNumber);
        killer.SetCustomProperties(new Hashtable { { RoomManager.KillsKey, kills[killer.ActorNumber] } });

        if (Mode == MatchMode.GunGame)
            AdvanceLadder(killer, weapon);
    }

    // Two kills moves you up a rung and swaps your weapon out from under you. Getting there
    // with the peel - the last rung - ends the match on the spot.
    void AdvanceLadder(Player killer, string weapon)
    {
        Rules.LadderStep step = Rules.Advance(RungOf(killer), RungKillsOf(killer), weapon,
                                              killsPerRung, WeaponLoadout.GunGameLadder);

        if (step.wins)
        {
            FinishMatch(killer);
            return;
        }

        rungs[killer.ActorNumber] = step.rung;
        rungKills[killer.ActorNumber] = step.rungKills;

        killer.SetCustomProperties(new Hashtable { { RungKey, step.rung }, { RungKillsKey, step.rungKills } });

        if (step.climbed)
            GiveLoadout(killer);
    }

    /// <summary>
    /// The parts of the rules that are decisions rather than plumbing. Split out because they
    /// are the parts most likely to be quietly wrong, and none of them need a server to check -
    /// a whole gun game can be played out against these in a batch mode test.
    /// </summary>
    public static class Rules
    {
        public struct LadderStep
        {
            public int rung;
            public int rungKills;
            public bool climbed;   // moved up, so the loadout has to be reissued
            public bool wins;      // killed with the last rung, so the match is over
        }

        public static LadderStep Advance(int rung, int rungKills, string weapon, int killsPerRung, string[] ladder)
        {
            LadderStep step = new LadderStep { rung = rung, rungKills = rungKills };

            if (ladder == null || ladder.Length == 0)
                return step;

            int top = ladder.Length - 1;

            // Winning is about the weapon in your hands, not the rung you are recorded at -
            // the two can disagree for a moment while a property is still in flight.
            if (rung >= top || weapon == ladder[top])
            {
                step.wins = true;
                return step;
            }

            step.rungKills = rungKills + 1;

            if (step.rungKills < killsPerRung)
                return step;

            step.rung = Mathf.Min(rung + 1, top);
            step.rungKills = 0;
            step.climbed = true;

            return step;
        }

        public static string[] LoadoutForRung(int rung, string[] ladder)
        {
            if (ladder == null || ladder.Length == 0)
                return WeaponLoadout.AllWeapons;

            return new[] { ladder[Mathf.Clamp(rung, 0, ladder.Length - 1)] };
        }

        /// Loadouts travel as one comma separated string, because a custom property wants a
        /// primitive and a string array is not one.
        public static string Serialise(IEnumerable<string> weapons) => string.Join(",", weapons);

        public static string[] Deserialise(string packed)
        {
            return string.IsNullOrEmpty(packed) ? WeaponLoadout.AllWeapons : packed.Split(',');
        }

        /// Highest score takes it; a tie leaves it with whoever got there first rather than
        /// whoever happens to sort last. Nobody wins a match where nobody scored.
        public static int WinnerIndex(int[] scores)
        {
            int best = -1;
            int bestScore = 0;

            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] > bestScore)
                {
                    bestScore = scores[i];
                    best = i;
                }
            }

            return best;
        }
    }

    Player LeaderByKills()
    {
        Player[] players = PhotonNetwork.PlayerList;
        int[] scores = new int[players.Length];

        for (int i = 0; i < players.Length; i++)
            scores[i] = RoomManager.GetStat(players[i], RoomManager.KillsKey);

        int winner = Rules.WinnerIndex(scores);
        return winner >= 0 ? players[winner] : null;
    }

    // The master's own tally first, the replicated value second. A property set a moment ago
    // has not come back from the server yet, so reading it would undo the increment.
    int RungOf(Player player)
    {
        return rungs.TryGetValue(player.ActorNumber, out int rung)
            ? rung
            : RoomManager.GetStat(player, RungKey);
    }

    int RungKillsOf(Player player)
    {
        return rungKills.TryGetValue(player.ActorNumber, out int count)
            ? count
            : RoomManager.GetStat(player, RungKillsKey);
    }

    static int Bump(Dictionary<int, int> counts, int actor)
    {
        counts.TryGetValue(actor, out int current);
        return current + 1;
    }

    static void SetRoom(Hashtable properties)
    {
        PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
    }

    static int RoomInt(string key, int fallback)
    {
        if (PhotonNetwork.InRoom
            && PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object value)
            && value is int i)
        {
            return i;
        }

        return fallback;
    }

    public static string NameOf(Player player)
    {
        if (player == null)
            return "someone";

        return string.IsNullOrWhiteSpace(player.NickName) ? $"Player {player.ActorNumber}" : player.NickName;
    }

    /// How far up the ladder a player is, for the HUD. Reads the replicated value so it works
    /// for everyone, not just the master.
    public static int LadderRung(Player player) => RoomManager.GetStat(player, RungKey);

    public static int LadderKills(Player player) => RoomManager.GetStat(player, RungKillsKey);

    public static int KillsToAdvance => Instance != null ? Instance.killsPerRung : 2;
}
