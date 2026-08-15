using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// The match rules, played out without a server.
//
// The phase machine needs Photon to do anything, but the decisions inside it don't - and the
// decisions are the part that goes quietly wrong. A gun game where the ladder skips a rung, or
// a deathmatch that rolls the same banana twice, both look fine until somebody is halfway
// through a match and it's too late to say anything useful about why.
public static class MatchCheck
{
    static readonly StringBuilder Log = new StringBuilder();
    static int failures;

    static void Check(bool ok, string label, string detail)
    {
        Log.AppendLine($"  {(ok ? "ok  " : "FAIL")}  {label,-46} {detail}");
        if (!ok)
            failures++;
    }

    public static void Run()
    {
        failures = 0;
        Log.Clear();

        LadderClimbsOneRungAtATime();
        LadderWinsOnlyOnTheLastWeapon();
        LadderCannotSkipPastTheTop();
        DeathmatchRollIsDistinct();
        LoadoutSurvivesTheRoundTrip();
        WinnerNeedsAScore();
        RungLoadoutIsASingleWeapon();
        SpawnsAvoidTheLiving();

        Debug.Log("[match] rules\n" + Log);
        Debug.Log(failures == 0 ? "[match] ===== ALL PASS =====" : $"[match] {failures} FAILURES");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Coming back should not put you next to somebody.
    ///
    /// The scoring is pure, so the interesting half of the spawn rules can be played out here
    /// without a scene, a map or a running game. Line of sight needs physics and is checked in
    /// the probe instead.
    /// </summary>
    static void SpawnsAvoidTheLiving()
    {
        // A line of pads with one player standing on the first.
        Vector3[] pads =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
            new Vector3(20f, 0f, 0f),
            new Vector3(30f, 0f, 0f),
        };

        List<Vector3> alone = new List<Vector3> { pads[0] };

        float[] scores = new float[pads.Length];
        for (int i = 0; i < pads.Length; i++)
            scores[i] = SpawnManager.Score(pads[i], alone);

        Check(scores[0] < scores[3], "a pad with somebody on it scores worst",
              $"{scores[0]:F0} against {scores[3]:F0}");

        Check(scores[3] > scores[2] && scores[2] > scores[1],
              "further away scores better all the way out",
              $"{scores[1]:F0} / {scores[2]:F0} / {scores[3]:F0}");

        // The nearest living player decides, not the average. Somebody on the far side of the
        // map should not make the person round the corner look further away than they are.
        List<Vector3> crowd = new List<Vector3> { pads[0], new Vector3(1000f, 0f, 0f) };

        Check(Mathf.Approximately(SpawnManager.Score(pads[1], alone),
                                  SpawnManager.Score(pads[1], crowd)),
              "a distant player does not make a close one safer",
              $"{SpawnManager.Score(pads[1], crowd):F0}");

        // Picking. The shortlist has to stay inside the best few and never fall off the end.
        float[] ranked = { 5f, 40f, 30f, 10f };
        bool everBad = false;
        bool sawSecond = false;

        for (int i = 0; i < 400; i++)
        {
            int pick = SpawnManager.PickFromBest(ranked, 2);

            // Best two are index 1 (40) and index 2 (30).
            if (pick != 1 && pick != 2)
                everBad = true;

            if (pick == 2)
                sawSecond = true;
        }

        Check(!everBad, "the shortlist never picks a bad pad", "400 draws stayed in the best two");

        // And it has to actually vary. Always taking the single best turns spawns into a fixed
        // rotation that can be camped exactly as effectively as a bad one.
        Check(sawSecond, "and it does not always pick the same one", "second best came up");

        Check(SpawnManager.PickFromBest(ranked, 99) >= 0 && SpawnManager.PickFromBest(new float[0], 3) == 0,
              "a silly shortlist and an empty map are survivable", "no exception");
    }

    static string[] Ladder => WeaponLoadout.GunGameLadder;

    // The whole mode, start to finish: two kills a rung, all the way up, win on the peel.
    static void LadderClimbsOneRungAtATime()
    {
        const int killsPerRung = 2;

        int rung = 0;
        int rungKills = 0;
        int kills = 0;
        bool won = false;
        List<string> climb = new List<string> { Ladder[0] };

        // Generous bound - if the rules ever stop terminating, this fails rather than hangs.
        for (int i = 0; i < 200 && !won; i++)
        {
            string holding = MatchState.Rules.LoadoutForRung(rung, Ladder)[0];
            MatchState.Rules.LadderStep step =
                MatchState.Rules.Advance(rung, rungKills, holding, killsPerRung, Ladder);

            kills++;
            won = step.wins;
            rung = step.rung;
            rungKills = step.rungKills;

            if (step.climbed)
                climb.Add(MatchState.Rules.LoadoutForRung(rung, Ladder)[0]);
        }

        // Every rung but the last needs its two kills; the last needs one.
        int expected = (Ladder.Length - 1) * killsPerRung + 1;

        Check(won, "a gun game ends", won ? $"after {kills} kills" : "never terminated");
        Check(kills == expected, "kills needed to win", $"{kills}, expected {expected}");
        Check(string.Join(" -> ", climb) == string.Join(" -> ", Ladder),
              "every rung is visited in order", string.Join(" -> ", climb));
    }

    // You win by killing with the peel, not by merely reaching it.
    static void LadderWinsOnlyOnTheLastWeapon()
    {
        string top = Ladder[Ladder.Length - 1];

        MatchState.Rules.LadderStep early = MatchState.Rules.Advance(0, 0, Ladder[0], 2, Ladder);
        Check(!early.wins, "first rung kill does not win", $"rung {early.rung}, {early.rungKills} on it");

        MatchState.Rules.LadderStep final = MatchState.Rules.Advance(Ladder.Length - 1, 0, top, 2, Ladder);
        Check(final.wins, "a peel kill wins", "wins");

        // The rung property and the weapon actually in hand can disagree while a property is
        // still in flight, so either being at the top has to count.
        MatchState.Rules.LadderStep lagging = MatchState.Rules.Advance(Ladder.Length - 1, 0, Ladder[0], 2, Ladder);
        Check(lagging.wins, "top rung wins even if the weapon lags", "wins");
    }

    static void LadderCannotSkipPastTheTop()
    {
        int top = Ladder.Length - 1;
        MatchState.Rules.LadderStep step = MatchState.Rules.Advance(top - 1, 1, Ladder[top - 1], 2, Ladder);

        Check(step.rung == top, "climbing from the penultimate rung", $"rung {step.rung} of {top}");
        Check(step.climbed, "a climb asks for a new loadout", step.climbed ? "yes" : "no");
    }

    // Three of the same banana would be a very short match.
    static void DeathmatchRollIsDistinct()
    {
        bool alwaysDistinct = true;
        bool alwaysRightSize = true;

        for (int attempt = 0; attempt < 400; attempt++)
        {
            string[] roll = WeaponLoadout.RandomSelection(3);

            if (roll.Length != 3)
                alwaysRightSize = false;

            HashSet<string> seen = new HashSet<string>(roll);
            if (seen.Count != roll.Length)
                alwaysDistinct = false;
        }

        Check(alwaysRightSize, "a deathmatch roll is three weapons", "400 rolls");
        Check(alwaysDistinct, "a deathmatch roll has no duplicates", "400 rolls");

        // Asking for more than exists must clamp rather than loop forever or repeat.
        string[] greedy = WeaponLoadout.RandomSelection(99);
        Check(greedy.Length == WeaponLoadout.AllWeapons.Length,
              "asking for too many clamps", $"{greedy.Length} of {WeaponLoadout.AllWeapons.Length}");
    }

    // Loadouts travel as one string because a custom property wants a primitive.
    static void LoadoutSurvivesTheRoundTrip()
    {
        string[] original = { "Pistol", "Sniper", "Peel" };
        string[] back = MatchState.Rules.Deserialise(MatchState.Rules.Serialise(original));

        Check(string.Join(",", back) == string.Join(",", original),
              "a loadout survives being packed", string.Join(",", back));

        string[] single = MatchState.Rules.Deserialise(MatchState.Rules.Serialise(new[] { "Peel" }));
        Check(single.Length == 1 && single[0] == "Peel", "a one weapon loadout survives", single[0]);

        // A client can see a player before it sees that player's loadout.
        string[] empty = MatchState.Rules.Deserialise(null);
        Check(empty != null && empty.Length > 0, "an absent loadout falls back", $"{empty.Length} weapons");
    }

    static void WinnerNeedsAScore()
    {
        Check(MatchState.Rules.WinnerIndex(new[] { 0, 0, 0 }) == -1,
              "nobody wins a scoreless match", "no winner");

        Check(MatchState.Rules.WinnerIndex(new[] { 2, 5, 3 }) == 1,
              "highest score wins", "index 1");

        Check(MatchState.Rules.WinnerIndex(new[] { 4, 4, 1 }) == 0,
              "a tie goes to whoever got there first", "index 0");

        Check(MatchState.Rules.WinnerIndex(new int[0]) == -1,
              "an empty room has no winner", "no winner");
    }

    static void RungLoadoutIsASingleWeapon()
    {
        bool allSingle = true;
        for (int rung = 0; rung < Ladder.Length; rung++)
        {
            string[] loadout = MatchState.Rules.LoadoutForRung(rung, Ladder);
            if (loadout.Length != 1 || loadout[0] != Ladder[rung])
                allSingle = false;
        }

        Check(allSingle, "each rung hands out exactly its weapon", $"{Ladder.Length} rungs");

        string[] clamped = MatchState.Rules.LoadoutForRung(999, Ladder);
        Check(clamped.Length == 1 && clamped[0] == Ladder[Ladder.Length - 1],
              "an out of range rung clamps to the top", clamped[0]);
    }
}
