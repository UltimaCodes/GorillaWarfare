using UnityEngine;

/// <summary>
/// The three crate tiers - names and their token cost are direct from the brief ("rotten crate,
/// ripe crate and holy crate which all cost different amounts"), continuing the same ripeness
/// vocabulary the guns already use (GunInfo.RipenessFor) rather than starting a fourth naming
/// scheme in the same game.
///
/// Odds are a first pass, the same caveat every other feel number in this project carries -
/// nobody has opened one yet. Shaped the way every real case-opening game shapes theirs though:
/// the top crate roughly triples the bottom crate's chance at the two rarest tiers rather than a
/// token flat bump, because "the expensive one is basically the same as the cheap one" would make
/// the whole tier system pointless.
/// </summary>
public readonly struct CrateInfo
{
    public readonly string Name;
    public readonly int Cost;
    readonly float[] odds; // parallel to CrateRarityInfo.All, must sum to 1

    CrateInfo(string name, int cost, float[] odds)
    {
        Name = name;
        Cost = cost;
        this.odds = odds;
    }

    public static readonly CrateInfo Rotten = new CrateInfo("Rotten Crate", 10,
        new[] { 0.60f, 0.27f, 0.10f, 0.025f, 0.005f });

    public static readonly CrateInfo Ripe = new CrateInfo("Ripe Crate", 50,
        new[] { 0.40f, 0.33f, 0.18f, 0.07f, 0.02f });

    public static readonly CrateInfo Holy = new CrateInfo("Holy Crate", 100,
        new[] { 0.20f, 0.30f, 0.28f, 0.16f, 0.06f });

    public static readonly CrateInfo[] All = { Rotten, Ripe, Holy };

    /// A real, honest roll - no rigged near-misses, no odds boosted after a dry streak. The
    /// number this returns is the actual result; CrateOpeningScreen's carousel dresses it up with
    /// other random filler on the way there, but never lies about where it's actually going to
    /// land partway through the spin.
    public CrateRarity Roll()
    {
        float roll = Random.value;
        float cumulative = 0f;

        for (int i = 0; i < odds.Length; i++)
        {
            cumulative += odds[i];
            if (roll < cumulative)
                return CrateRarityInfo.All[i];
        }

        return CrateRarityInfo.All[CrateRarityInfo.All.Length - 1];
    }

    public float OddsFor(CrateRarity rarity) => odds[System.Array.IndexOf(CrateRarityInfo.All, rarity)];
}
