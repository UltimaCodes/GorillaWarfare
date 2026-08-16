using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public enum MatchMode
{
    Deathmatch = 0,
    GunGame = 1,

    /// Red against blue. Same rules as deathmatch, except your side's kills are what count and
    /// you cannot shoot the people on it.
    TeamDeathmatch = 2,
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

    /// Which side won, in a team mode, or -1. Separate from WinnerKey rather than overloading
    /// it - an actor number and a team index are different things and packing both into one
    /// property is how you end up drawing "player 1 wins" when team 1 won.
    public const string WinningTeamKey = "winTeam";


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
    [SerializeField] int killsPerRung = 2;

    public static MatchState Instance { get; private set; }

    /// Newest last. The HUD draws it; nothing else should mutate it.
    public static readonly List<FeedEntry> Feed = new List<FeedEntry>();

    public enum FeedKind
    {
        Kill,
        Join,
        Leave,
    }

    /// <summary>
    /// One line in the corner of the screen.
    ///
    /// Kills, joins and leaves share a list rather than having one each, because they compete
    /// for the same few lines of screen and the only thing that decides what you see is which
    /// happened most recently. Two separate feeds would either overlap or need a third thing to
    /// arbitrate between them.
    /// </summary>
    public struct FeedEntry
    {
        public FeedKind kind;

        /// Whoever did it - the killer, or the person arriving or leaving.
        public string actor;

        /// Who it was done to. Kills only.
        public string subject;

        public string weapon;
        public bool headshot;

        /// Which kill feed line to use. Rolled by the victim and replicated, so everyone reads
        /// the same sentence.
        public byte flavour;

        /// True when the local player is either end of it, so the HUD can pick it out.
        public bool involvesYou;

        /// True when the killer is getting their own back on whoever last killed them.
        public bool revenge;

        public float at;
    }

    static void Push(FeedEntry entry)
    {
        // Unscaled, because hitstop drags scaled time to a crawl at exactly the moment a kill
        // is added - the entry would sit there not ageing while the world stuttered.
        entry.at = Time.unscaledTime;
        Feed.Add(entry);

        if (Feed.Count > 32)
            Feed.RemoveRange(0, Feed.Count - 32);
    }

    // The master's own tally. Custom properties do not update locally until the server echoes
    // them back, so incrementing straight off the replicated value loses a kill whenever two
    // land inside one round trip. Only the master writes scores, so its cache is the truth.
    readonly Dictionary<int, int> kills = new Dictionary<int, int>();
    readonly Dictionary<int, int> deaths = new Dictionary<int, int>();
    readonly Dictionary<int, int> rungKills = new Dictionary<int, int>();
    readonly Dictionary<int, int> rungs = new Dictionary<int, int>();

    public static float RespawnDelay => Instance != null ? Instance.respawnSeconds : 3f;

    /// How long warmup runs. The HUD paces the loadout reveal off this, and had its own copy of
    /// the number until the probe shortened one of them and the two silently disagreed.
    public static float WarmupLength => Instance != null ? Instance.warmupSeconds : 8f;

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

    /// The winning side, or -1. Only meaningful in a team mode.
    public static int WinningTeam => RoomInt(WinningTeamKey, -1);

    public struct Award
    {
        public string title;
        public string who;
        public string detail;
    }

    /// <summary>
    /// A line or two about how the match went, beyond who won.
    ///
    /// Computed from replicated properties on whichever client is asking, so everyone sees the
    /// same list without anything being sent. Every award needs a non-zero winner - handing out
    /// HEADHUNTER for zero headshots is worse than not handing it out, because it reads as the
    /// game not having noticed.
    ///
    /// Deliberately includes an award for being bad at it. The person who died most is going to
    /// find out either way and it is funnier coming from the game.
    /// </summary>
    public static List<Award> Awards()
    {
        List<Award> found = new List<Award>();

        if (!PhotonNetwork.InRoom)
            return found;

        Add(found, "TOP BANANA", RoomManager.KillsKey, "kills");
        Add(found, "HEADHUNTER", RoomManager.HeadshotsKey, "headshots");
        Add(found, "ON A ROLL", RoomManager.BestStreakKey, "in a row");
        Add(found, "CRASH TEST DUMMY", RoomManager.DeathsKey, "deaths");

        return found;
    }

    static void Add(List<Award> into, string title, string key, string unit)
    {
        Player best = null;
        int most = 0;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            int value = RoomManager.GetStat(player, key);

            if (value <= most)
                continue;

            most = value;
            best = player;
        }

        if (best == null || most <= 0)
            return;

        into.Add(new Award { title = title, who = NameOf(best), detail = $"{most} {unit}" });
    }

    public static Player Winner
    {
        get
        {
            int actor = RoomInt(WinnerKey, -1);
            return actor >= 0 && PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
        }
    }


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

        // No warmup here any more, and this was the whole bug behind "there's no warmup, you get
        // thrown straight into the match".
        //
        // OnJoinedRoom fires the moment the room is created, which is while you are still
        // standing in the lobby picking a mode and waiting for people. The warmup clock is a
        // deadline against server time, so it started ticking there - and by the time anybody
        // actually loaded into the game, thirty seconds of lobby had gone by and an eight second
        // warmup had expired four times over. Tick had already moved the phase to Live before
        // the first player existed.
        //
        // It starts when the game scene loads instead. See BeginMatchIfFresh.
    }



    public override void OnLeftRoom()
    {
        Feed.Clear();
        lastKilledBy.Clear();

        // Reset the echo tracking as well as the tallies. Rejoining with `requested` still
        // pointing at the last match's phase makes the first transition of the next one wait
        // for an echo that already came and went.
        requested = MatchPhase.Warmup;
        awaitingEcho = false;
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

        // Whatever the old master had asked for either landed or died with them.
        awaitingEcho = false;

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            kills[player.ActorNumber] = RoomManager.GetStat(player, RoomManager.KillsKey);
            deaths[player.ActorNumber] = RoomManager.GetStat(player, RoomManager.DeathsKey);
            rungKills[player.ActorNumber] = RoomManager.GetStat(player, RungKillsKey);
            rungs[player.ActorNumber] = RoomManager.GetStat(player, RungKey);
        }

        Debug.Log($"[match] took over as master mid {Phase}, restored {kills.Count} scores.");
    }

    /// <summary>
    /// The host changed something about the room - almost always the mode.
    ///
    /// Nothing listened for this before, and it was the whole reason gun game didn't work.
    /// BeginWarmup runs from OnJoinedRoom, which is the moment the room is created and long
    /// before anyone picks a mode, so everybody was handed a deathmatch loadout. Switching to
    /// gun game afterwards changed the label and nothing else: you kept the three weapons you'd
    /// already been given, which is indistinguishable from the ladder being broken.
    /// </summary>
    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!changed.ContainsKey(ModeKey))
            return;

        Debug.Log($"[match] mode is now {Mode}, reissuing loadouts");

        if (!PhotonNetwork.IsMasterClient)
            return;

        // Sides only exist in a team mode, and AssignTeams clears them outside one - so this
        // handles both directions without a special case for either.
        PlayerColours.AssignTeams();

        // Gun game starts everyone at the bottom of the ladder. Carrying a rung over from a
        // deathmatch would be meaningless anyway.
        foreach (Player player in PhotonNetwork.PlayerList)
        {
            player.SetCustomProperties(new Hashtable { { RungKey, 0 }, { RungKillsKey, 0 } });
            rungs[player.ActorNumber] = 0;
            rungKills[player.ActorNumber] = 0;

            GiveLoadout(player);
        }
    }

    // Anyone arriving mid-match needs a loadout, and in deathmatch that is the set this match
    // rolled - not whatever they would have picked for themselves.
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        // Everyone posts the message; only the master hands out the weapons.
        Push(new FeedEntry { kind = FeedKind.Join, actor = NameOf(newPlayer) });

        if (!PhotonNetwork.IsMasterClient)
            return;

        // PUN never clears player properties, not even between rooms - they follow you into the
        // next game you join. Somebody who finished a gun game four rungs up therefore arrived
        // holding the sniper, and the master, reading that same stale property, agreed with it.
        // Wipe first, then work out what they should be carrying.
        newPlayer.SetCustomProperties(new Hashtable { { RungKey, 0 }, { RungKillsKey, 0 } });
        rungs[newPlayer.ActorNumber] = 0;
        rungKills[newPlayer.ActorNumber] = 0;

        // Rebalance rather than dropping them on the smaller side, which drifts badly once
        // people start leaving and ends in four against one.
        PlayerColours.AssignTeams();

        // No phase guard any more. Skipping this while the scoreboard was up meant anyone who
        // joined between matches stood around with nothing in their hands until the next warmup.
        GiveLoadout(newPlayer);
    }

    /// <summary>
    /// Someone left. Photon has always fired this and nothing was listening, so people
    /// disappeared mid-fight with no explanation - which reads as a bug in the game rather than
    /// as somebody closing it.
    /// </summary>
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Push(new FeedEntry { kind = FeedKind.Leave, actor = NameOf(otherPlayer) });
    }

    // The phase we last asked the server for. Room properties do not update locally until the
    // server echoes them, so for the round trip after a transition Phase and TimeLeft both
    // still read the old values - without this, Update fires the same transition every frame
    // until the echo lands, and every one of those is a property write.
    MatchPhase requested = MatchPhase.Warmup;
    bool awaitingEcho;

    void Update()
    {
        if (!PhotonNetwork.IsMasterClient || !PhotonNetwork.InRoom)
            return;

        // No clock in the sandbox. A match that ends and starts a scoreboard countdown while
        // you are measuring a weapon is an interruption, not a game.
        if (Sandbox.Active)
        {
            if (Phase != MatchPhase.Live)
            {
                Requested(MatchPhase.Live);
                SetRoom(new Hashtable
                {
                    { PhaseKey, (int)MatchPhase.Live },
                    { EndsAtKey, PhotonNetwork.Time + 99999.0 },
                });
            }

            return;
        }

        // No phase in the room at all. This is the whole reason there was no warmup.
        //
        // Phase falls back to Warmup when the key is missing and TimeLeft falls back to zero,
        // so an unstarted match reads as "a warmup that has already run out" - and the switch
        // below promotes it to Live on the very first frame. You were thrown into the match
        // because the game believed the warmup had already happened.
        //
        // Gated on the scene rather than on joining the room, because the phase is meaningless
        // while everyone is still in the lobby picking a mode. The clock is a deadline against
        // server time, so starting it there burned the whole warmup on the lobby.
        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PhaseKey))
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex
                == RoomManager.gameSceneIndex)
            {
                Debug.Log("[match] map is up, starting warmup");
                BeginWarmup();
            }

            return;
        }

        if (awaitingEcho)
        {
            if (Phase != requested)
                return;

            awaitingEcho = false;
        }

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

    void Requested(MatchPhase phase)
    {
        requested = phase;
        awaitingEcho = true;
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
        headshots.Clear();
        streak.Clear();
        bestStreak.Clear();
        lastKilledBy.Clear();

        // Seeded to zero rather than merely emptied, and this is the bug where everybody started
        // the next gun game holding the peel.
        //
        // RungFor prefers the master's own tally and falls back to the replicated property when
        // the tally has no entry. Clearing the dictionary removed the entry, so the fallback
        // fired - and the property still held last match's rung, because SetCustomProperties a
        // few lines below has not echoed yet. Every player was handed the weapon for the rung
        // they finished on.
        foreach (Player seat in PhotonNetwork.PlayerList)
        {
            rungs[seat.ActorNumber] = 0;
            rungKills[seat.ActorNumber] = 0;
        }

        foreach (Player player in PhotonNetwork.PlayerList)
        {
            player.SetCustomProperties(new Hashtable
            {
                { RoomManager.KillsKey, 0 },
                { RoomManager.DeathsKey, 0 },
                { RoomManager.HeadshotsKey, 0 },
                { RoomManager.BestStreakKey, 0 },
                { RungKey, 0 },
                { RungKillsKey, 0 },
            });
        }

        // Sides are picked fresh every match, so nobody is stuck losing with the same four
        // people all evening.
        PlayerColours.AssignTeams();

        Requested(MatchPhase.Warmup);
        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Warmup },
            { WinnerKey, -1 },
            { WinningTeamKey, -1 },
            { EndsAtKey, PhotonNetwork.Time + warmupSeconds },
        });

        foreach (Player player in PhotonNetwork.PlayerList)
            GiveLoadout(player);
    }

    void BeginLive()
    {
        float length = Mode == MatchMode.GunGame ? gunGameSeconds : deathmatchSeconds;

        Requested(MatchPhase.Live);
        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Live },
            { EndsAtKey, PhotonNetwork.Time + length },
        });
    }

    void FinishMatch(Player winner)
    {
        int team = -1;

        // In a team mode the side is the result and the individual is a footnote. Worked out
        // here rather than read off a running counter, so the number on the results screen is
        // the same sum the scoreboard has been showing all match.
        if (Mode == MatchMode.TeamDeathmatch)
        {
            int red = PlayerColours.TeamScore(0);
            int blue = PlayerColours.TeamScore(1);

            // A draw stays a draw. Inventing a winner out of a tie is worse than saying nobody
            // won, and in a room of four people a tie happens often enough to matter.
            team = red == blue ? -1 : red > blue ? 0 : 1;
        }

        Requested(MatchPhase.Over);
        SetRoom(new Hashtable
        {
            { PhaseKey, (int)MatchPhase.Over },
            { WinnerKey, winner != null ? winner.ActorNumber : -1 },
            { WinningTeamKey, team },
            { EndsAtKey, PhotonNetwork.Time + scoreboardSeconds },
        });
    }

    /// What a player should be carrying right now, under the current mode.
    /// <summary>
    /// What a player should be carrying right now, under the current mode.
    ///
    /// One weapon either way. Gun game hands you whatever rung you're on; deathmatch rolls a
    /// fresh one, which is why this is called again on every respawn rather than once a match -
    /// dying is how you get a different gun.
    ///
    /// Static and public because two different people call it: the master when someone climbs
    /// a rung, and the player themselves when they spawn. Both computing it the same way is
    /// what stops the two disagreeing.
    /// </summary>
    public static string[] WeaponsFor(Player player)
    {
        // The sandbox hands you everything. Its whole job is comparing weapons, and doing that
        // one respawn at a time would make it useless for the thing it exists for.
        if (Sandbox.Active)
            return WeaponLoadout.Everything;

        if (Mode == MatchMode.GunGame)
            return Rules.LoadoutForRung(RungFor(player), WeaponLoadout.GunGameLadder);

        return WeaponLoadout.RandomSelection(1);
    }

    /// <summary>
    /// Which rung a player is on, asked in the one place it gets asked while the answer is
    /// changing underneath you.
    ///
    /// The master's own tally beats the replicated property, and that distinction is the whole
    /// bug: SetCustomProperties sends an op and waits for the server to echo it, so the moment
    /// after the master pushes your new rung the property still reads the rung you just left.
    /// Handing out a weapon from that value gave you the gun you already had. It looked like
    /// nothing happened when you climbed - and then you'd die, respawn, and finally get the new
    /// weapon, because by then the echo had landed.
    ///
    /// Only trusted on the master, which is the only client that maintains the tally. Everybody
    /// else reads the replicated value, which for them is the only truth there is.
    /// </summary>
    public static int RungFor(Player player)
    {
        if (PhotonNetwork.IsMasterClient && Instance != null && player != null
            && Instance.rungs.TryGetValue(player.ActorNumber, out int rung))
            return rung;

        return RoomManager.GetStat(player, RungKey);
    }

    void GiveLoadout(Player player)
    {
        player.SetCustomProperties(
            new Hashtable { { PlayerController.LoadoutKey, Rules.Serialise(WeaponsFor(player)) } });
    }

    // ---- kills ----

    /// <summary>
    /// Called on every client from the victim's death RPC. Only the master acts on it; everyone
    /// runs the feed side so the message appears at the same moment for all of them.
    /// </summary>
    /// <summary>
    /// Who last killed whom, kept by every client rather than replicated.
    ///
    /// Every client sees every kill in the same order, so every client can work out on its own
    /// that this kill is a reprisal - and they will all agree, which is the only property that
    /// matters. Replicating it would be a property write per kill to compute something everyone
    /// already knows.
    /// </summary>
    static readonly Dictionary<int, int> lastKilledBy = new Dictionary<int, int>();

    public static void ReportKill(Player killer, Player victim, string weapon, bool headshot,
                                 byte flavour = 0)
    {
        bool revenge = killer != null && victim != null
                       && lastKilledBy.TryGetValue(killer.ActorNumber, out int owed)
                       && owed == victim.ActorNumber;

        if (victim != null)
        {
            if (killer != null && killer != victim)
                lastKilledBy[victim.ActorNumber] = killer.ActorNumber;
            else
                lastKilledBy.Remove(victim.ActorNumber);
        }

        // Settled, so the next kill between these two is not also revenge.
        if (revenge)
            lastKilledBy.Remove(killer.ActorNumber);

        Push(new FeedEntry
        {
            kind = FeedKind.Kill,
            actor = killer != null ? NameOf(killer) : string.Empty,
            subject = NameOf(victim),
            weapon = weapon,
            headshot = headshot,
            flavour = flavour,
            revenge = revenge,
            involvesYou = IsLocal(killer) || IsLocal(victim),
        });

        // The feed still says it happened - it did, and hiding it would look like a bug - but
        // nothing counts until the match is live.
        //
        // Warmup kills used to score, which meant the countdown was the best time in the match
        // to farm: everybody is bunched near a spawn, nobody is expecting it, and in gun game
        // you could be two rungs up before the word LIVE had left the screen. The warmup exists
        // so people can land and get their bearings, and it cannot do that while it is also the
        // most profitable thirty seconds available.
        if (Phase != MatchPhase.Live)
            return;

        if (Instance != null && PhotonNetwork.IsMasterClient)
            Instance.ScoreKill(killer, victim, weapon, headshot);
    }

    static bool IsLocal(Player player) => player != null && player.IsLocal;

    readonly Dictionary<int, int> headshots = new Dictionary<int, int>();
    readonly Dictionary<int, int> streak = new Dictionary<int, int>();
    readonly Dictionary<int, int> bestStreak = new Dictionary<int, int>();

    void ScoreKill(Player killer, Player victim, string weapon, bool headshot)
    {
        if (Phase != MatchPhase.Live)
            return;

        if (victim != null)
        {
            // Dying ends a run. Kept on the master rather than read back off the property,
            // because the property has not echoed yet at this point.
            streak[victim.ActorNumber] = 0;

            deaths[victim.ActorNumber] = Bump(deaths, victim.ActorNumber);
            victim.SetCustomProperties(new Hashtable { { RoomManager.DeathsKey, deaths[victim.ActorNumber] } });

            // A different gun every life, which is the whole shape of deathmatch here - you
            // don't pick your weapon, dying is how you get another one. Rolled by the master
            // rather than by the player, so there is exactly one client deciding what anybody
            // carries. Two writers to the same property is a race, and the race was being lost
            // by whoever had just joined.
            //
            // Above the suicide check on purpose: falling off the map is still a life ended.
            if (Mode == MatchMode.Deathmatch)
                GiveLoadout(victim);
        }

        // Falling off the map, or shooting yourself, is a death and nothing else.
        if (killer == null || killer == victim)
            return;

        kills[killer.ActorNumber] = Bump(kills, killer.ActorNumber);

        if (headshot)
            headshots[killer.ActorNumber] = Bump(headshots, killer.ActorNumber);

        streak.TryGetValue(killer.ActorNumber, out int run);
        run++;
        streak[killer.ActorNumber] = run;

        bestStreak.TryGetValue(killer.ActorNumber, out int best);
        bestStreak[killer.ActorNumber] = Mathf.Max(best, run);

        headshots.TryGetValue(killer.ActorNumber, out int heads);

        killer.SetCustomProperties(new Hashtable
        {
            { RoomManager.KillsKey, kills[killer.ActorNumber] },
            { RoomManager.HeadshotsKey, heads },
            { RoomManager.BestStreakKey, bestStreak[killer.ActorNumber] },
        });

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

        bool climbed = step.rung != rungs.GetValueOrDefault(killer.ActorNumber, 0);

        rungs[killer.ActorNumber] = step.rung;
        rungKills[killer.ActorNumber] = step.rungKills;

        // The rung only goes on the wire when it actually moves. Writing it on every kill meant
        // every client saw a rung property change every time anybody died, and the HUD announced
        // a promotion that had not happened.
        Hashtable progress = new Hashtable { { RungKillsKey, step.rungKills } };

        if (climbed)
            progress[RungKey] = step.rung;

        killer.SetCustomProperties(progress);

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
