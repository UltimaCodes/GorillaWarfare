using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A weapon skin: one finish that goes on any weapon. "Universal finishes but make sure theyre
/// REALLY unique and interesting and not boring" (Ryaan, 2026-09-30). So each is a mix of a few
/// things the finish shader (Custom/WeaponFinish) can do - paint, a real pattern from Kenney's
/// Pattern Pack, chrome, a glow that pulses, a hue that travels, an edge light, a glint, a jelly
/// wobble - and a particle aura from Kenney's Particle Pack streaming off the weapon.
///
/// One asset per finish in Resources/Finishes, so any of it can be retuned in the inspector.
/// Made by Tools/Gorilla Warfare/Build the weapon finishes the first time; that only ever adds
/// the ones that are missing, so hand edits to an existing finish stay.
///
/// "ill make actual weapon skin models" - `models` is where those go: a model for a weapon, by
/// its key (Pistol, Rifle...), drawn instead of the stock one whenever this finish is on it.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Weapon Finish")]
public class WeaponFinish : ScriptableObject
{
    public enum Motion
    {
        Rise,       // floats up off the weapon - embers, bubbles, spirits
        Fall,       // drops away under gravity - drips, snow, sparks
        Drift,      // wanders about in the air near it - dust, wisps
        Orbit,      // circles the weapon's length - a ring of stars
        Sparkle,    // pops in and out where it is - glints
    }

    [System.Serializable]
    public class ModelOverride
    {
        [Tooltip("The weapon's key - Pistol, Rifle, Shotgun, Sniper, Gatling, Flamer, Pineapple, Peel.")]
        public string weapon;
        public GameObject model;
    }

    [Tooltip("Never shown. What saves and the network store, so it has to stay put while the name changes.")]
    public string key;

    public string displayName;

    [TextArea] public string flavour;

    public CrateRarity rarity;

    [Header("Paint")]
    public Color paint = Color.white;
    [Tooltip("0 keeps the weapon's own colours under everything else, 1 paints over them (keeping their light and dark).")]
    [Range(0f, 1f)] public float repaint = 1f;
    [Range(0f, 1f)] public float metallic;
    [Range(0f, 1f)] public float smoothness = 0.3f;

    [Header("Pattern")]
    [Tooltip("A tile from Assets/Art/Patterns. White takes the pattern colour.")]
    public Texture2D pattern;
    [Tooltip("Alpha is how strongly the pattern shows.")]
    public Color patternColour = new Color(0f, 0f, 0f, 1f);
    public float patternRepeats = 6f;
    [Tooltip("Repeats per second - a pattern that crawls along the weapon.")]
    public Vector2 patternScroll;
    [Tooltip("The pattern's colour glows as well.")]
    public float patternGlow;

    [Header("Glow")]
    public Color glow = Color.black;
    public float glowStrength;
    [Tooltip("Pulses per second. 0 is a steady glow.")]
    public float pulseSpeed;
    [Range(0f, 1f)] public float pulseDepth;

    [Header("Hue")]
    [Tooltip("Turns of the colour wheel per second - every colour on it cycles.")]
    public float hueCycle;
    [Tooltip("Turns of the colour wheel from grip to tip - a rainbow along it.")]
    public float hueSpread;

    [Header("Edge light")]
    public Color rim = Color.black;
    public float rimStrength;
    public float rimPower = 3f;

    [Header("Glint")]
    [Tooltip("Alpha is the glint's strength.")]
    public Color glint = new Color(1f, 1f, 1f, 0f);
    [Tooltip("Seconds between glints running along the weapon. 0 is none.")]
    public float glintEvery;
    [Range(0.02f, 1f)] public float glintWidth = 0.15f;

    [Header("Wobble")]
    public float wobble;
    public float wobbleSpeed = 6f;

    [Header("Aura")]
    [Tooltip("A sprite from Resources/Particles/Finish. Empty is no aura.")]
    public Texture2D auraSprite;
    public Color auraColour = Color.white;
    [Tooltip("Particles a second.")]
    public float auraRate;
    public float auraSize = 0.05f;
    public float auraLife = 0.8f;
    public Motion auraMotion = Motion.Rise;
    [Tooltip("Adds light rather than covering what's behind it - fire, sparks, stars. Off for smoke and bubbles.")]
    public bool auraGlows = true;
    [Tooltip("Colours cycle through the hue as they're born, the way the finish's own hue does.")]
    public bool auraRainbow;

    [Header("Your own models")]
    public ModelOverride[] models = new ModelOverride[0];

    /// What scrapping one copy pays, by how rare it is.
    public int ScrapValue => ScrapValueFor(rarity);

    public static int ScrapValueFor(CrateRarity rarity) => rarity switch
    {
        CrateRarity.Scrap => 2,
        CrateRarity.Sprout => 5,
        CrateRarity.Primal => 12,
        CrateRarity.Mythic => 30,
        CrateRarity.Apex => 75,
        _ => 0,
    };

    public GameObject ModelFor(string weapon)
    {
        if (models == null)
            return null;

        foreach (ModelOverride entry in models)
        {
            if (entry != null && entry.model != null && entry.weapon == weapon)
                return entry.model;
        }

        return null;
    }
}

/// <summary>
/// Every finish in the game, from Resources/Finishes, in rarity order.
/// </summary>
public static class FinishCatalog
{
    static WeaponFinish[] all;
    static Dictionary<string, WeaponFinish> byKey;

    public static IReadOnlyList<WeaponFinish> All
    {
        get
        {
            Load();
            return all;
        }
    }

    public static WeaponFinish Find(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        Load();
        return byKey.TryGetValue(key, out WeaponFinish finish) ? finish : null;
    }

    public static List<WeaponFinish> OfRarity(CrateRarity rarity)
    {
        Load();
        List<WeaponFinish> found = new List<WeaponFinish>();
        foreach (WeaponFinish finish in all)
        {
            if (finish.rarity == rarity)
                found.Add(finish);
        }

        return found;
    }

    /// A finish of this rarity, at random - or of the nearest rarity that has any, so a crate
    /// can never land on nothing.
    public static WeaponFinish Pick(CrateRarity rarity)
    {
        for (int step = 0; step < CrateRarityInfo.All.Length; step++)
        {
            foreach (int direction in new[] { -1, 1 })
            {
                int index = System.Array.IndexOf(CrateRarityInfo.All, rarity) + step * direction;
                if (index < 0 || index >= CrateRarityInfo.All.Length)
                    continue;

                List<WeaponFinish> pool = OfRarity(CrateRarityInfo.All[index]);
                if (pool.Count > 0)
                    return pool[Random.Range(0, pool.Count)];
            }
        }

        return null;
    }

    static void Load()
    {
        if (all != null)
            return;

        List<WeaponFinish> loaded = new List<WeaponFinish>(Resources.LoadAll<WeaponFinish>("Finishes"));
        loaded.RemoveAll(f => f == null || string.IsNullOrEmpty(f.key));
        loaded.Sort((a, b) => a.rarity != b.rarity ? a.rarity.CompareTo(b.rarity) : string.CompareOrdinal(a.displayName, b.displayName));
        all = loaded.ToArray();

        byKey = new Dictionary<string, WeaponFinish>();
        foreach (WeaponFinish finish in all)
            byKey[finish.key] = finish;
    }
}
