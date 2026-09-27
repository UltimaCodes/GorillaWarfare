using UnityEngine;

/// <summary>
/// The five outcome tiers a crate can land on. Standard gray/green/blue/purple/gold ladder -
/// Diablo II and WoW popularized it decades ago and it reads instantly to anyone who has played
/// a loot-bearing game since, which is exactly why it's kept rather than invented from scratch -
/// but the names are this game's own. Deliberately not sharing a single word with StyleScore's
/// rank tiers (PEELING/RIPE/GOING BANANAS/FULL SILVERBACK/RAMPAGE) or the multikill callouts
/// (DOUBLE PEEL/BUNCH KILL/GORILLA WARFARE/GOING FERAL) - three different systems all naming
/// escalating tiers of the same five-ish rungs would blur into each other with any overlap.
/// </summary>
public enum CrateRarity
{
    Scrap,
    Sprout,
    Primal,
    Mythic,
    Apex,
}

public static class CrateRarityInfo
{
    public static string NameFor(CrateRarity rarity) => rarity switch
    {
        CrateRarity.Scrap => "SCRAP",
        CrateRarity.Sprout => "SPROUT",
        CrateRarity.Primal => "PRIMAL",
        CrateRarity.Mythic => "MYTHIC",
        CrateRarity.Apex => "APEX",
        _ => "?",
    };

    public static Color ColorFor(CrateRarity rarity) => rarity switch
    {
        CrateRarity.Scrap => new Color(0.62f, 0.62f, 0.65f),
        CrateRarity.Sprout => new Color(0.32f, 0.85f, 0.35f),
        CrateRarity.Primal => new Color(0.25f, 0.55f, 1f),
        CrateRarity.Mythic => new Color(0.68f, 0.32f, 0.95f),
        CrateRarity.Apex => new Color(1f, 0.82f, 0.15f),
        _ => Color.white,
    };

    /// How big a deal the reveal should look/sound like it is - the escalating-payoff idea this
    /// whole project already leans on everywhere (a headshot gets more Juice than a body shot, a
    /// kill gets more than a hit), applied to a fifth axis instead of a new one. Purely a scale on
    /// existing effects (particle count, shake, hold time, sound layers) - see
    /// CrateOpeningScreen.Reveal - not a separate set of assets per tier.
    public static float Weight(CrateRarity rarity) => rarity switch
    {
        CrateRarity.Scrap => 0.15f,
        CrateRarity.Sprout => 0.35f,
        CrateRarity.Primal => 0.55f,
        CrateRarity.Mythic => 0.8f,
        CrateRarity.Apex => 1f,
        _ => 0f,
    };

    public static readonly CrateRarity[] All =
    {
        CrateRarity.Scrap, CrateRarity.Sprout, CrateRarity.Primal, CrateRarity.Mythic, CrateRarity.Apex,
    };
}
