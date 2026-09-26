using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>
/// One number for how well you are playing right now, not just how many kills you have.
///
/// Reworked entirely from the old slide-only combo meter - reported directly as the most hated
/// thing on the HUD ("I hate the bar the most honestly"), with DMC and ULTRAKILL named as what a
/// style meter should actually feel like. This is the unification: the movement chain
/// (PlayerMovement.SlideChain) is one input among several rather than the whole system. A
/// headshot, a no-scope, a point-blank or long-range finish, an airborne or blade kill, switching
/// weapons between kills, a killstreak, a multikill, or landing a kill while a slide chain is
/// still hot all push the multiplier up; going quiet, getting hit, or smashing into a wall bring
/// it back down. The running total this earns is the new decider for deathmatch - see
/// MatchState.LeaderByScore - because "who has the most kills" was never really the question this
/// game wanted to ask, "who is playing the best right now" was.
///
/// The multiplier itself starts climbing the moment you land a hit, not only on a kill - "your
/// multiplier starts when you damage someone and do stuff but when you kill someone you actually
/// get the points" (RegisterHitLanded vs. ApplyKillGain, which is the only place score itself
/// ever changes). A whole gunfight builds toward the finish now, instead of the multiplier
/// sitting cold and silent until the exact instant something dies.
///
/// Local only, mine-only, added by PlayerController.Start the same way PlayerMovement and
/// SpeedRush are - classifying a kill (were you scoped, how close was it) only ever needs data
/// this client already has, and each client owns writing its own score into its own custom
/// property rather than racing anyone else for it (contrast RoomManager.KillsKey, which the
/// master writes precisely because two different killers could otherwise race over the same
/// victim's death count - no such race exists over your own score).
/// </summary>
public class StyleScore : MonoBehaviour
{
    PlayerController owner;
    PlayerMovement movement;

    float multiplier = 1f;
    float comboExpiresAt = -99f;
    int score;
    string lastKillWeapon;

    public const float MaxMultiplier = 8f;
    const float ComboWindow = 4f;      // seconds of no kill before the multiplier starts bleeding off
    const float DecayRate = 1.3f;      // multiplier lost per second once it's bleeding
    const int BaseKillScore = 100;

    // Getting hit costs you a piece of the multiplier outright; smashing into a wall costs more
    // and comes with its own effect - reported as two different things ("gets hurt when you hit
    // something" for the wall, a separate note for taking a hit), so they get two different
    // weights rather than sharing one penalty.
    const float HitPenalty = 0.75f;
    const float WallSmashPenalty = 0.4f;

    /// How close a kill has to land to count as point blank. Public - SingleShotGun classifies
    /// a dummy kill inline (see RegisterDummyKill) rather than through the pendingShots handshake
    /// RegisterKill uses, and needs the same threshold rather than a second copy of the number.
    public const float PointBlankRange = 3.5f;

    /// The other end of the same measurement - point blank's opposite, for a confirmed kill from
    /// real distance. Same reasoning as PointBlankRange for being public and the same first-pass
    /// caveat every other feel number in this project carries: picked without a real map to
    /// measure against, see roadmap.md's Unverified section.
    public const float LongRangeThreshold = 18f;

    /// How often landing a hit (not a kill) can add to the multiplier. Reported directly: "your
    /// multiplier starts when you damage someone and do stuff... when you kill someone you
    /// actually get the points" - hits need to start the climb, but without a cooldown a fast
    /// weapon emptying a magazine into one target would trivially max the multiplier before the
    /// fight is even decided, which would make landing the actual kill feel like an afterthought
    /// instead of the payoff.
    const float HitCreditCooldown = 0.35f;
    const float HitGain = 0.12f;

    /// A shot's classification only matters if the target actually dies to roughly that same
    /// shot - a no-scope tapped in from across the map a full clip before somebody finally died
    /// to a completely different bullet shouldn't count as a no-scope kill.
    const float ShotMemory = 1.5f;

    public float Multiplier => multiplier;
    public int Score => score;
    public bool Active => multiplier > 1.001f;
    public string Rank => RankFor(multiplier);

    /// Full the instant a kill lands, empty by the time the multiplier would start decaying -
    /// the same shape PlayerMovement.ChainWindowFraction already used for the slide rank, which
    /// is exactly the DMC/ULTRAKILL "keep it up or lose it" read the reference asked for.
    public float DecayFraction => Active
        ? Mathf.Clamp01((comboExpiresAt - Time.unscaledTime) / ComboWindow)
        : 0f;

    // Banana/gorilla themed rather than letters, on purpose - a lettered rank is the one thing
    // that would have made this read as a straight ULTRAKILL/DMC copy rather than this game's
    // own idea wearing the same shape of mechanic. Escalates the same way a magazine's own
    // ripeness does before turning into something else entirely at the top.
    static readonly (float threshold, string name)[] Tiers =
    {
        (1f,   "PEELING"),
        (1.6f, "RIPE"),
        (2.6f, "GOING BANANAS"),
        (4f,   "FULL SILVERBACK"),
        (6f,   "RAMPAGE"),
    };

    public static string RankFor(float multiplier)
    {
        string name = Tiers[0].name;

        for (int i = 0; i < Tiers.Length; i++)
        {
            if (multiplier >= Tiers[i].threshold)
                name = Tiers[i].name;
        }

        return name;
    }

    /// <summary>
    /// Same five names, a different axis - the banked, cumulative match score rather than the
    /// live per-hit multiplier. Built for the tab scoreboard, which only ever has each player's
    /// replicated total (RoomManager.StyleScoreKey) to read, never their live multiplier - that's
    /// local combat state on their own client, not something worth replicating every frame.
    ///
    /// Thresholds are a first pass, anchored against the one number this game's economy already
    /// treats as "a genuinely good match": GameHud.RewardScoreScale (2500) is where the round-end
    /// token curve is already close to its cap. RAMPAGE sits just past that on purpose. Untested
    /// against real matches, same as every other tuning number in this project - see Unverified.
    /// </summary>
    static readonly (int threshold, string name)[] ScoreTiers =
    {
        (0,    "PEELING"),
        (500,  "RIPE"),
        (1200, "GOING BANANAS"),
        (2000, "FULL SILVERBACK"),
        (3000, "RAMPAGE"),
    };

    public static string RankForScore(int score)
    {
        string name = ScoreTiers[0].name;

        for (int i = 0; i < ScoreTiers.Length; i++)
        {
            if (score >= ScoreTiers[i].threshold)
                name = ScoreTiers[i].name;
        }

        return name;
    }

    struct ShotContext
    {
        public string weapon;
        public bool aiming;
        public float distance;
        public float at;
    }

    // Keyed by target actor number rather than a single "last shot" - a shotgun blast or a burst
    // can be mid-flight at more than one target in the same frame in principle, and this way a
    // kill always classifies off the shot that was actually aimed at that specific victim.
    readonly Dictionary<int, ShotContext> pendingShots = new Dictionary<int, ShotContext>();

    float lastHitCreditAt = -99f;

    // Quake/UT have run "Double Kill"/"Multi Kill"/"Ultra Kill" for decades - close enough to
    // this game's own multikill concept that reusing those exact words would read as borrowed
    // rather than as this game's own idea, the same reasoning the rank tiers avoid lettered
    // ULTRAKILL-style names. "RAMPAGE" specifically is already spoken for by the rank tier above,
    // so it's off the table here too even though it's the obvious classic-arena-shooter word.
    static readonly (int count, string name)[] MultikillNames =
    {
        (2, "DOUBLE PEEL"),
        (3, "BUNCH KILL"),
        (4, "GORILLA WARFARE"),
        (5, "GOING FERAL"),
    };

    static string MultikillNameFor(int count)
    {
        string name = null;

        for (int i = 0; i < MultikillNames.Length; i++)
        {
            if (count >= MultikillNames[i].count)
                name = MultikillNames[i].name;
        }

        return name;
    }

    void Awake()
    {
        owner = GetComponent<PlayerController>();
        movement = GetComponent<PlayerMovement>();

        // Continues this player's running match total rather than starting over at 0. A fresh
        // StyleScore is built on every respawn (PlayerController.Start's own AddComponent, same
        // as SpeedRush - there is no previous instance to carry state over from), but the score
        // itself is meant to be a running total for the whole match, not for one life. Without
        // this seed, the first kill after any respawn would Publish() a small fresh number
        // straight over the room property, silently erasing everything scored before dying - a
        // real bug in the exact stat MatchState.LeaderByScore uses to decide who wins deathmatch,
        // caught while wiring the round-end token reward to this same value and seeing it read
        // back 0 for a player who had genuinely already scored that match.
        if (PhotonNetwork.LocalPlayer != null)
            score = RoomManager.GetStat(PhotonNetwork.LocalPlayer, RoomManager.StyleScoreKey);
    }

    /// Called by the shooter, right where damage is dealt, with everything needed to classify a
    /// kill if this turns out to be the one that lands it. Distance and aim state both only ever
    /// exist on the shooter's own client at the moment of the shot - by the time a kill is
    /// confirmed the RPC round trip has already happened and that context is gone, so it has to
    /// be captured here rather than reconstructed later.
    public void RecordShot(int targetActor, string weapon, bool aiming, float distance)
    {
        pendingShots[targetActor] = new ShotContext
        {
            weapon = weapon,
            aiming = aiming,
            distance = distance,
            at = Time.unscaledTime,
        };
    }

    /// A shot (or any other damaging hit) connected but didn't finish the target off. The
    /// multiplier now starts climbing here rather than waiting for the kill - reported directly:
    /// "your multiplier starts when you damage someone and do stuff but when you kill someone
    /// you actually get the points." Never touches score (that's still kill-only, see
    /// ApplyKillGain) and never publishes (same reasoning RegisterHitTaken already has - nothing
    /// here changes the one property that's actually networked). Gated by HitCreditCooldown, not
    /// by who or what was hit - a dummy counts the same as a real player, since building the
    /// multiplier through a fight is the point being rewarded, not the specific target.
    public void RegisterHitLanded()
    {
        if (MatchState.Phase != MatchPhase.Live)
            return;

        if (Time.unscaledTime - lastHitCreditAt < HitCreditCooldown)
            return;

        lastHitCreditAt = Time.unscaledTime;

        multiplier = Mathf.Min(MaxMultiplier, multiplier + HitGain);
        comboExpiresAt = Time.unscaledTime + ComboWindow;
    }

    /// Called once a kill is confirmed - see PlayerController.RPC_Died, which every client
    /// receives including the killer's own.
    public void RegisterKill(int victimActor, string weapon, bool headshot)
    {
        // Same rule ScoreKill itself follows: nothing counts before the match goes live, or the
        // warmup becomes the best time to farm a multiplier for free.
        if (MatchState.Phase != MatchPhase.Live)
            return;

        bool noscope = false;
        bool pointBlank = false;
        bool longRange = false;

        if (pendingShots.TryGetValue(victimActor, out ShotContext shot)
            && Time.unscaledTime - shot.at < ShotMemory)
        {
            GunInfo info = Resources.Load<GunInfo>($"Guns/{weapon}");
            noscope = info != null && info.canAim && !shot.aiming;
            pointBlank = shot.distance < PointBlankRange;
            longRange = shot.distance > LongRangeThreshold;
        }

        pendingShots.Remove(victimActor);

        ApplyKillGain(weapon, headshot, noscope, pointBlank, longRange);
    }

    /// Same scoring a real kill gets, for the one kind of kill that never reaches RegisterKill at
    /// all. A training dummy (or anything else IDamageable that isn't a player) resolves its own
    /// death synchronously, in the same call that dealt the damage - see IDamageable.TakeDamage -
    /// so there's no RPC round trip to wait on and nothing to stash in pendingShots. It's also the
    /// only kill the sandbox can ever produce, which is why the style meter reads as permanently
    /// broken there without this: RegisterKill only ever fires from PlayerController.RPC_Died,
    /// and solo sandbox testing never generates one of those.
    public void RegisterDummyKill(string weapon, bool headshot, bool noscope, bool pointBlank,
                                  bool longRange = false)
    {
        if (MatchState.Phase != MatchPhase.Live)
            return;

        ApplyKillGain(weapon, headshot, noscope, pointBlank, longRange);
    }

    /// The scoring both RegisterKill and RegisterDummyKill share, once each has worked out its
    /// own headshot/noscope/point-blank/long-range read - by two different routes, since a real
    /// kill's classification only exists at the moment of the shot (pendingShots) while a dummy
    /// kill's exists synchronously at the point of death, but from here on there's nothing left
    /// that depends on which route it came from.
    ///
    /// Fleshed out well past the original five bonuses - reported directly as "only a few combos
    /// and its not that fun at all." Airborne, blade finish, killstreak and multikill are all
    /// computed from data this component already has cached or can read off `owner` directly,
    /// rather than needing more parameters threaded through from every call site the way
    /// headshot/noscope/point-blank/long-range have to be (that data genuinely only exists at
    /// the moment of the shot, on the shooter's own client - these don't).
    void ApplyKillGain(string weapon, bool headshot, bool noscope, bool pointBlank, bool longRange)
    {
        bool weaponSwap = !string.IsNullOrEmpty(lastKillWeapon) && lastKillWeapon != weapon;
        lastKillWeapon = weapon;

        bool airborne = movement != null && !movement.Grounded;
        bool blade = weapon == "Peel";

        int chain = movement != null ? movement.SlideChain : 0;
        int killstreak = owner != null ? owner.Killstreak : 0;
        int multikill = owner != null ? owner.Multikill : 0;

        // Built as a breakdown rather than folded straight into one number - reported directly:
        // "I told you to merge the speed multipliers, the slider boosts, etc into that but you
        // didn't" against a version that only ever showed the combined total. Each entry's shown
        // value is 1 + that bonus's own share, framed as its own "x" the way the reference image
        // listed its own contributions (a named reason next to a number), even though the
        // underlying math is additive rather than each factor truly multiplying the last - the
        // total below is computed from `gain` directly, this list exists to explain it.
        lastBreakdown.Clear();
        float gain = 1f;

        if (headshot) { gain += 0.35f; lastBreakdown.Add(new BreakdownEntry { label = "HEADSHOT", shownMultiplier = 1.35f }); }
        if (noscope) { gain += 0.5f; lastBreakdown.Add(new BreakdownEntry { label = "NO SCOPE", shownMultiplier = 1.5f }); }
        if (pointBlank) { gain += 0.3f; lastBreakdown.Add(new BreakdownEntry { label = "POINT BLANK", shownMultiplier = 1.3f }); }

        // Point blank's opposite - a kill confirmed from real range. Mutually exclusive with
        // point blank by construction (one shot can't be both close and far), so no risk of
        // double-dipping the same distance from two directions.
        if (longRange) { gain += 0.4f; lastBreakdown.Add(new BreakdownEntry { label = "LONG RANGE", shownMultiplier = 1.4f }); }

        if (weaponSwap) { gain += 0.25f; lastBreakdown.Add(new BreakdownEntry { label = "WEAPON SWAP", shownMultiplier = 1.25f }); }

        // Ground pound can only ever land while airborne, so a ground-pound kill always carries
        // this too - intentional, not a loophole: jumping into a slam is exactly the kind of
        // committed, risky move this bonus exists to reward.
        if (airborne) { gain += 0.3f; lastBreakdown.Add(new BreakdownEntry { label = "AIRBORNE", shownMultiplier = 1.3f }); }

        // The signature melee weapon, not just "any melee" - this game only has the one.
        if (blade) { gain += 0.45f; lastBreakdown.Add(new BreakdownEntry { label = "BLADE FINISH", shownMultiplier = 1.45f }); }

        if (chain > 0)
        {
            float chainGain = 0.15f * Mathf.Min(chain, 5);
            gain += chainGain;
            lastBreakdown.Add(new BreakdownEntry { label = $"SLIDE CHAIN x{chain}", shownMultiplier = 1f + chainGain });
        }

        // Consecutive kills without dying - a slower-building, harder-earned escalation than the
        // in-the-moment bonuses above it, capped the same way the slide chain is so one very long
        // life doesn't run away with the multiplier forever.
        if (killstreak > 1)
        {
            float streakGain = 0.1f * Mathf.Min(killstreak - 1, 5);
            gain += streakGain;
            lastBreakdown.Add(new BreakdownEntry { label = $"KILL STREAK x{killstreak}", shownMultiplier = 1f + streakGain });
        }

        // Kills landed within the same short window (PlayerController.multikillWindow) - a
        // named callout rather than a bare number, same shape the rank tiers use, so a fast
        // double/triple reads as its own moment rather than just a bigger version of a normal
        // kill.
        if (multikill > 1)
        {
            float multiGain = 0.2f * Mathf.Min(multikill - 1, 4);
            gain += multiGain;
            string multiName = MultikillNameFor(multikill) ?? $"MULTIKILL x{multikill}";
            lastBreakdown.Add(new BreakdownEntry { label = multiName, shownMultiplier = 1f + multiGain });
        }

        // Highest contribution first, matching the reference's own descending order.
        lastBreakdown.Sort((a, b) => b.shownMultiplier.CompareTo(a.shownMultiplier));
        lastBreakdownAt = Time.unscaledTime;

        multiplier = Mathf.Min(MaxMultiplier, multiplier + gain);
        comboExpiresAt = Time.unscaledTime + ComboWindow;

        score += Mathf.RoundToInt(BaseKillScore * multiplier);

        Publish();
    }

    public struct BreakdownEntry
    {
        public string label;
        public float shownMultiplier;
    }

    readonly List<BreakdownEntry> lastBreakdown = new List<BreakdownEntry>();
    float lastBreakdownAt = -99f;

    /// How long the breakdown stays up after a kill, same shape as everything else on this HUD
    /// that shows and then clears rather than lingering forever.
    public const float BreakdownDuration = 3.5f;

    public IReadOnlyList<BreakdownEntry> LastBreakdown => lastBreakdown;
    public bool BreakdownActive => lastBreakdown.Count > 0
                                   && Time.unscaledTime - lastBreakdownAt < BreakdownDuration;

    /// A shot landed on you. Softer than a wall smash - you're still in the fight, you just paid
    /// for it. Doesn't publish - this only ever touches the multiplier, never the score, and
    /// SetCustomProperties is a real network write; sending one on every single hit taken
    /// (potentially several a second under fire) for a value that never changed would be pure
    /// waste.
    public void RegisterHitTaken()
    {
        if (!Active)
            return;

        multiplier = Mathf.Max(1f, multiplier * HitPenalty);
    }

    /// Momentum meeting a wall. See PlayerMovement.OnControllerColliderHit for the threshold and
    /// the effect that goes with this. Same reasoning as RegisterHitTaken - no publish, this
    /// never changes the score.
    public void RegisterWallSmash()
    {
        multiplier = Mathf.Max(1f, multiplier * WallSmashPenalty);

        // No grace period - a smash this hard means the decay clock should already read as
        // expired, not merely reset to a fresh four seconds.
        comboExpiresAt = Time.unscaledTime;
    }

    void Update()
    {
        // Unscaled throughout this file - a kill's own hitstop drags Time.timeScale down right
        // when the multiplier is freshest, and every deadline/timer here needs to keep running
        // through that rather than stall with it (see working-notes.md's own standing rule: HUD
        // timers, the kill feed, Juice itself - all unscaled, for the same reason).
        if (multiplier > 1f && Time.unscaledTime > comboExpiresAt)
            multiplier = Mathf.Max(1f, multiplier - DecayRate * Time.unscaledDeltaTime);

        // A new match's own warmup resets everyone's replicated score (MatchState.BeginWarmup),
        // but this component itself only ever gets rebuilt on death/respawn - a player who
        // happens to be alive and standing around when a new match starts keeps the same
        // StyleScore instance, local `score` and all. Left alone, their first kill of the new
        // match would publish last match's total plus one kill's worth, undoing the property
        // reset the instant it landed. Same class of bug this project has hit before with PUN
        // never clearing custom properties on its own - see working-notes.md.
        if (MatchState.Phase == MatchPhase.Warmup && score != 0)
        {
            score = 0;
            multiplier = 1f;
            comboExpiresAt = -99f;
            lastKillWeapon = null;
            lastBreakdown.Clear();
            pendingShots.Clear();
            Publish();
        }
    }

    void Publish()
    {
        if (PhotonNetwork.LocalPlayer != null)
        {
            PhotonNetwork.LocalPlayer.SetCustomProperties(
                new Hashtable { { RoomManager.StyleScoreKey, score } });
        }
    }
}
