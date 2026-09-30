using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The weapon finishes - one WeaponFinish asset each in Resources/Finishes - made the first time,
/// then Ryaan's to retune in the inspector. Only ever adds a finish that's missing; one that's
/// there is left exactly as it is. GW_FINISH_REFRESH=1 writes every one of them again from this
/// file (for while they're still being designed here, not after anyone's edited them).
///
/// "Universal finishes but make sure theyre REALLY unique and interesting and not boring." Six a
/// rarity, and each one is a different idea rather than the same one recoloured: the cheap ones
/// are paint jobs with one twist, the middle ones real patterns and metals, the top ones glow,
/// move and throw particles. Everything is the weapon's own model and texture coloured by
/// Custom/WeaponFinish, Kenney's patterns (Assets/Art/Patterns) and Kenney's particle sprites
/// (Resources/Particles/Finish) - no textures made here.
/// </summary>
public static class FinishLibraryBuilder
{
    const string Folder = "Assets/Resources/Finishes";

    // The crates' chests wear finishes too - their own, never in a crate, so kept apart from
    // FinishCatalog's folder and referenced by the crate shop prefab directly.
    public const string LooksFolder = "Assets/Art/Crates/Looks";
    const string Patterns = "Assets/Art/Patterns";
    const string Sprites = "Assets/Resources/Particles/Finish";

    [MenuItem("Tools/Gorilla Warfare/Build the weapon finishes")]
    public static void Run()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(LooksFolder);
        bool refresh = Environment.GetEnvironmentVariable("GW_FINISH_REFRESH") == "1";
        int added = 0, rewritten = 0;

        List<(string folder, string key, Action<WeaponFinish> fill)> all = new List<(string, string, Action<WeaponFinish>)>();
        foreach ((string key, Action<WeaponFinish> fill) in Library())
            all.Add((Folder, key, fill));
        foreach ((string key, Action<WeaponFinish> fill) in CrateLooks())
            all.Add((LooksFolder, key, fill));

        foreach ((string folder, string key, Action<WeaponFinish> fill) in all)
        {
            string path = $"{folder}/{key}.asset";
            WeaponFinish finish = AssetDatabase.LoadAssetAtPath<WeaponFinish>(path);

            if (finish != null && !refresh)
                continue;

            bool isNew = finish == null;
            if (isNew)
                finish = ScriptableObject.CreateInstance<WeaponFinish>();
            else
                Reset(finish);

            finish.key = key;
            fill(finish);

            if (isNew)
            {
                AssetDatabase.CreateAsset(finish, path);
                added++;
            }
            else
            {
                EditorUtility.SetDirty(finish);
                rewritten++;
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[skins] finishes: {added} added, {rewritten} rewritten, in {Folder}");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Reset(WeaponFinish finish)
    {
        WeaponFinish blank = ScriptableObject.CreateInstance<WeaponFinish>();
        WeaponFinish.ModelOverride[] models = finish.models;
        EditorUtility.CopySerialized(blank, finish);
        finish.models = models;   // your own models survive a refresh
        UnityEngine.Object.DestroyImmediate(blank);
    }

    // ---------------------------------------------------------------- the finishes

    static IEnumerable<(string, Action<WeaponFinish>)> Library()
    {
        // ---- SCRAP: a paint job with one twist each ----

        yield return ("unripe", f => Name(f, "Green Around the Stalk", CrateRarity.Scrap, "Picked a week early. Still works.")
            .Paint(Hex(0x3E8E2E), smooth: 0.2f));

        yield return ("bruised", f => Name(f, "Bruised", CrateRarity.Scrap, "Dropped it once. It's fine. Mostly.")
            .Paint(Hex(0xF2C94C), smooth: 0.25f)
            .Pattern("pattern_24", Hex(0x4A2E14), 0.85f, repeats: 4f));

        yield return ("plantain", f => Name(f, "Plantain", CrateRarity.Scrap, "The serious cousin.")
            .Paint(Hex(0xE9DDB8), smooth: 0.45f)
            .Rim(Hex(0xFFF4D6), 0.25f));

        yield return ("charcoal", f => Name(f, "Charcoal", CrateRarity.Scrap, "Goes with everything. Especially fire.")
            .Paint(Hex(0x1E1F24), metallic: 0.3f, smooth: 0.55f)
            .Rim(Hex(0x8A93A6), 0.35f));

        yield return ("bubblegum", f => Name(f, "Bubblegum", CrateRarity.Scrap, "Chewed, not stirred.")
            .Paint(Hex(0xFF8FC7), smooth: 0.85f));

        yield return ("leaf-litter", f => Name(f, "Leaf Litter", CrateRarity.Scrap, "You were never here.")
            .Paint(Hex(0x6B7A3A), smooth: 0.15f)
            .Pattern("pattern_32", Hex(0x2F3A1C), 0.9f, repeats: 3f));

        // ---- SPROUT: real patterns, real metal ----

        yield return ("gunmetal", f => Name(f, "Gunmetal", CrateRarity.Sprout, "Heavy for a fruit.")
            .Paint(Hex(0x3A3F47), metallic: 0.9f, smooth: 0.75f)
            .Glint(Color.white, 0.35f, every: 4f));

        yield return ("zebra", f => Name(f, "Zebra Crossing", CrateRarity.Sprout, "Look both ways.")
            .Paint(Hex(0xF5F5F0), smooth: 0.35f)
            .Pattern("pattern_83", Hex(0x121212), 1f, repeats: 2.5f));

        yield return ("tiger", f => Name(f, "Tiger Tiger", CrateRarity.Sprout, "Burning bright.")
            .Paint(Hex(0xF08A24), smooth: 0.35f)
            .Pattern("pattern_27", Hex(0x1A1108), 0.95f, repeats: 2f));

        yield return ("giraffe", f => Name(f, "Long Neck", CrateRarity.Sprout, "Sees you coming.")
            .Paint(Hex(0xF3E3B5), smooth: 0.3f)
            .Pattern("pattern_80", Hex(0x9A5A22), 1f, repeats: 3f));

        yield return ("chequered", f => Name(f, "Chequered Flag", CrateRarity.Sprout, "First past the post.")
            .Paint(Color.white, smooth: 0.6f)
            .Pattern("pattern_16", Hex(0x111111), 1f, repeats: 4f));

        yield return ("candy-cane", f => Name(f, "Candy Striped", CrateRarity.Sprout, "Mind the hook.")
            .Paint(Hex(0xFFF8F5), smooth: 0.9f)
            .Pattern("pattern_02", Hex(0xD7263D), 1f, repeats: 3f)
            .Rim(Color.white, 0.3f));

        // ---- PRIMAL: metal that shines, colour that glows ----

        yield return ("chrome", f => Name(f, "Chrome Banana", CrateRarity.Primal, "You can see yourself in it. You look good.")
            .Paint(Hex(0xE6E9EF), metallic: 1f, smooth: 0.95f)
            .Rim(Color.white, 0.45f, power: 4f)
            .Glint(Color.white, 0.8f, every: 3f));

        yield return ("gold-leaf", f => Name(f, "Gold Leaf", CrateRarity.Primal, "Worth its weight.")
            .Paint(Hex(0xF4C542), metallic: 1f, smooth: 0.85f)
            .Glint(Hex(0xFFF1B8), 1f, every: 2.5f, width: 0.12f)
            .Aura("star_07", Hex(0xFFD86B), rate: 8f, size: 0.11f, life: 0.9f, WeaponFinish.Motion.Sparkle));

        yield return ("neon-hive", f => Name(f, "Neon Hive", CrateRarity.Primal, "Open all night.")
            .Paint(Hex(0x0E0E14), smooth: 0.7f)
            .Pattern("pattern_72", Hex(0xFF2E9A), 1f, repeats: 5f, glow: 2.5f)
            .Pulse(0.5f, 0.3f));

        yield return ("frostbite", f => Name(f, "Frostbite", CrateRarity.Primal, "Don't lick it.")
            .Paint(Hex(0xBFE6FF), smooth: 0.9f)
            .Rim(Hex(0xE6FBFF), 1.3f, power: 2.5f)
            .Aura("star_04", Color.white, rate: 20f, size: 0.08f, life: 1.4f, WeaponFinish.Motion.Fall, glows: false));

        yield return ("toxic", f => Name(f, "Toxic Waste", CrateRarity.Primal, "Glows in the dark. Don't ask why.")
            .Paint(Hex(0x14200F), smooth: 0.6f)
            .Pattern("pattern_30", Hex(0x8CFF3A), 1f, repeats: 3f, glow: 2f, scroll: new Vector2(0f, 0.35f))
            .Aura("circle_02", Hex(0xA8FF5E), rate: 14f, size: 0.09f, life: 1.2f, WeaponFinish.Motion.Rise));

        yield return ("lovebug", f => Name(f, "Lovebug", CrateRarity.Primal, "It's not you. It's the banana.")
            .Paint(Hex(0xFFB3C8), smooth: 0.7f)
            .Pattern("pattern_75", Hex(0xE8174A), 1f, repeats: 5f, glow: 0.8f)
            .Pulse(1.2f, 0.55f)
            .Aura("symbol_01", Hex(0xFF4F7B), rate: 8f, size: 0.1f, life: 1.3f, WeaponFinish.Motion.Rise));

        // ---- MYTHIC: it moves ----

        yield return ("molten-core", f => Name(f, "Molten Core", CrateRarity.Mythic, "Handle with oven gloves.")
            .Paint(Hex(0x1A0F0A), smooth: 0.35f)
            .Pattern("pattern_79", Hex(0xFF6A00), 1f, repeats: 3f, glow: 3.5f)
            .Pulse(0.7f, 0.45f)
            .Glow(Hex(0xFF3D00), 0.25f)
            .Aura("fire_01", Hex(0xFF8A1E), rate: 40f, size: 0.07f, life: 0.9f, WeaponFinish.Motion.Rise));

        yield return ("deep-space", f => Name(f, "Deep Space", CrateRarity.Mythic, "Somewhere out there a banana is looking back.")
            .Paint(Hex(0x090B1F), metallic: 0.2f, smooth: 0.9f)
            .Pattern("pattern_56", Hex(0xCFE3FF), 1f, repeats: 3f, glow: 2.5f, scroll: new Vector2(0.05f, 0.08f))
            .Rim(Hex(0x5B3BFF), 1.2f, power: 3f)
            .Aura("magic_01", Hex(0xB9CCFF), rate: 7f, size: 0.17f, life: 2f, WeaponFinish.Motion.Orbit));

        yield return ("thunderstruck", f => Name(f, "Thunderstruck", CrateRarity.Mythic, "Crackles when it's quiet.")
            .Paint(Hex(0x0B1B3D), metallic: 0.5f, smooth: 0.8f)
            .Pattern("pattern_08", Hex(0x7DF3FF), 1f, repeats: 5f, glow: 3f, scroll: new Vector2(0f, 2.2f))
            .Glow(Hex(0x39D5FF), 0.5f)
            .Pulse(6f, 0.75f)
            .Rim(Hex(0x9BEFFF), 2f, power: 2.5f)
            .Aura("spark_05", Hex(0x8FEAFF), rate: 22f, size: 0.16f, life: 0.35f, WeaponFinish.Motion.Sparkle));

        yield return ("jelly", f => Name(f, "Jelly", CrateRarity.Mythic, "Wobbly. Deadly. Wobbly.")
            .Paint(Hex(0xE23BD6), smooth: 0.97f)
            .Rim(Hex(0xFFB8F6), 1.6f, power: 2f)
            .Wobble(0.02f, 7f)
            .Aura("circle_03", Hex(0xFFC6F7), rate: 12f, size: 0.08f, life: 1.4f, WeaponFinish.Motion.Rise, glows: false));

        yield return ("hazard-pay", f => Name(f, "Hazard Pay", CrateRarity.Mythic, "Keep clear of the business end.")
            .Paint(Hex(0xFFC91A), smooth: 0.5f)
            .Pattern("pattern_01", Hex(0x151515), 1f, repeats: 4f, scroll: new Vector2(0f, 0.6f))
            .Glow(Hex(0xFFB000), 0.6f)
            .Pulse(2f, 0.9f)
            .Glint(Color.white, 0.6f, every: 1.8f));

        yield return ("haunted", f => Name(f, "Haunted", CrateRarity.Mythic, "Something died in here. It was a banana.")
            .Paint(Hex(0x6E8570), smooth: 0.6f)
            .Rim(Hex(0x6DFFB0), 2.6f, power: 2f)
            .Glow(Hex(0x4DFF9A), 0.3f)
            .Pulse(0.35f, 0.6f)
            .Wobble(0.006f, 3f)
            .Aura("smoke_02", Hex(0x9DFFCC), rate: 10f, size: 0.24f, life: 1.8f, WeaponFinish.Motion.Drift, glows: false));

        // ---- APEX: all of it at once ----

        yield return ("prismatic", f => Name(f, "Prismatic", CrateRarity.Apex, "Every colour at once. Showing off.")
            .Paint(Hex(0xFF3B3B), metallic: 0.85f, smooth: 0.9f)
            .Glow(Hex(0xFF3B3B), 0.55f)
            .Hue(cycle: 0.25f, spread: 1.5f)
            .Rim(Color.white, 0.8f, power: 3f)
            .Glint(Color.white, 0.8f, every: 2f)
            .Aura("star_06", Color.white, rate: 16f, size: 0.11f, life: 0.9f, WeaponFinish.Motion.Sparkle, rainbow: true));

        yield return ("solar-flare", f => Name(f, "Solar Flare", CrateRarity.Apex, "Don't look directly at it.")
            .Paint(Hex(0xFFE27A), smooth: 0.6f)
            .Glow(Hex(0xFFB22E), 2.4f)
            .Pulse(0.8f, 0.3f)
            .Rim(Hex(0xFF5A00), 3f, power: 2f)
            .Glint(Color.white, 1f, every: 1.5f)
            .Aura("flame_05", Hex(0xFF9A2E), rate: 40f, size: 0.13f, life: 0.7f, WeaponFinish.Motion.Rise));

        yield return ("void-walker", f => Name(f, "Void Walker", CrateRarity.Apex, "It's bigger on the inside.")
            .Paint(Hex(0x050508), smooth: 0.95f)
            .Pattern("pattern_36", Hex(0x7B2BFF), 0.8f, repeats: 3f, glow: 1.2f, scroll: new Vector2(0.1f, -0.15f))
            .Rim(Hex(0xA13BFF), 3f, power: 4f)
            .Pulse(0.4f, 0.4f)
            .Glow(Hex(0x2A0A55), 0.4f)
            .Aura("twirl_02", Hex(0xC27BFF), rate: 16f, size: 0.2f, life: 1.1f, WeaponFinish.Motion.Orbit));

        yield return ("disco-inferno", f => Name(f, "Disco Inferno", CrateRarity.Apex, "The party starts when you walk in.")
            .Paint(Hex(0xFF2BD6), metallic: 0.6f, smooth: 0.9f)
            .Pattern("pattern_16", Hex(0x2BFFF1), 1f, repeats: 6f, glow: 1.5f)
            .Hue(cycle: 0.6f, spread: 0f)
            .Glint(Color.white, 1f, every: 1f, width: 0.1f)
            .Aura("star_08", Color.white, rate: 18f, size: 0.13f, life: 0.6f, WeaponFinish.Motion.Sparkle, rainbow: true));

        yield return ("crown-jewel", f => Name(f, "Crown Jewel", CrateRarity.Apex, "Fit for a king. Owned by a gorilla.")
            .Paint(Hex(0xE8B923), metallic: 1f, smooth: 0.9f)
            .Pattern("pattern_61", Hex(0x10D98A), 1f, repeats: 5f, glow: 1.6f)
            .Glint(Hex(0xFFF6CF), 1f, every: 2f)
            .Rim(Hex(0xFFE08A), 0.8f)
            .Aura("star_09", Hex(0xFFE9A0), rate: 12f, size: 0.13f, life: 1f, WeaponFinish.Motion.Sparkle));

        yield return ("glitch", f => Name(f, "Glitch", CrateRarity.Apex, "ERR: BANANA NOT FOUND")
            .Paint(Hex(0x12F2FF), smooth: 0.5f)
            .Pattern("pattern_45", Hex(0xFF1FA2), 1f, repeats: 4f, glow: 1.8f, scroll: new Vector2(1.7f, 0.4f))
            .Pulse(9f, 0.6f)
            .Glow(Hex(0x12F2FF), 0.5f)
            .Wobble(0.008f, 28f)
            .Aura("window_04", Color.white, rate: 16f, size: 0.09f, life: 0.3f, WeaponFinish.Motion.Sparkle, rainbow: true));
    }

    /// The chests: Rotten gone mouldy with flies round it, Ripe banana-gold with a glint, Holy
    /// white chrome that glows, with light rising off it.
    static IEnumerable<(string, Action<WeaponFinish>)> CrateLooks()
    {
        yield return ("crate-rotten", f => Name(f, "Rotten", CrateRarity.Scrap, "")
            .Paint(Hex(0x5B6B2A), smooth: 0.2f, repaint: 0.6f)
            .Pattern("pattern_24", Hex(0x2E3514), 0.8f, repeats: 3f)
            .Rim(Hex(0x9BC24A), 0.5f)
            .Aura("dirt_01", Hex(0x1C1C1C), rate: 12f, size: 0.05f, life: 1.6f, WeaponFinish.Motion.Orbit, glows: false));

        yield return ("crate-ripe", f => Name(f, "Ripe", CrateRarity.Sprout, "")
            .Paint(Hex(0xFFD23A), metallic: 0.2f, smooth: 0.6f, repaint: 0.55f)
            .Rim(Hex(0xFFF0A0), 0.5f)
            .Glint(Color.white, 0.8f, every: 2.2f)
            .Aura("star_07", Hex(0xFFE27A), rate: 6f, size: 0.1f, life: 1f, WeaponFinish.Motion.Sparkle));

        yield return ("crate-holy", f => Name(f, "Holy", CrateRarity.Apex, "")
            .Paint(Hex(0xF4F8FF), metallic: 0.85f, smooth: 0.9f, repaint: 0.75f)
            .Glow(Hex(0xCFE8FF), 0.5f)
            .Pulse(0.6f, 0.4f)
            .Rim(Color.white, 2.2f, power: 2.5f)
            .Glint(Color.white, 1f, every: 1.6f)
            .Aura("star_05", Hex(0xDDEEFF), rate: 14f, size: 0.07f, life: 1.6f, WeaponFinish.Motion.Rise));
    }

    // ---------------------------------------------------------------- a little language for the above

    static WeaponFinish Name(WeaponFinish f, string name, CrateRarity rarity, string flavour)
    {
        f.displayName = name;
        f.rarity = rarity;
        f.flavour = flavour;
        return f;
    }

    static WeaponFinish Paint(this WeaponFinish f, Color colour, float metallic = 0f, float smooth = 0.3f, float repaint = 1f)
    {
        f.paint = colour;
        f.metallic = metallic;
        f.smoothness = smooth;
        f.repaint = repaint;
        return f;
    }

    static WeaponFinish Pattern(this WeaponFinish f, string tile, Color colour, float amount, float repeats, float glow = 0f, Vector2 scroll = default)
    {
        f.pattern = Load<Texture2D>($"{Patterns}/{tile}.png");
        f.patternColour = new Color(colour.r, colour.g, colour.b, amount);
        f.patternRepeats = repeats;
        f.patternGlow = glow;
        f.patternScroll = scroll;
        return f;
    }

    static WeaponFinish Glow(this WeaponFinish f, Color colour, float strength)
    {
        f.glow = colour;
        f.glowStrength = strength;
        return f;
    }

    static WeaponFinish Pulse(this WeaponFinish f, float speed, float depth)
    {
        f.pulseSpeed = speed;
        f.pulseDepth = depth;
        return f;
    }

    static WeaponFinish Hue(this WeaponFinish f, float cycle, float spread)
    {
        f.hueCycle = cycle;
        f.hueSpread = spread;
        return f;
    }

    static WeaponFinish Rim(this WeaponFinish f, Color colour, float strength, float power = 3f)
    {
        f.rim = colour;
        f.rimStrength = strength;
        f.rimPower = power;
        return f;
    }

    static WeaponFinish Glint(this WeaponFinish f, Color colour, float strength, float every, float width = 0.15f)
    {
        f.glint = new Color(colour.r, colour.g, colour.b, strength);
        f.glintEvery = every;
        f.glintWidth = width;
        return f;
    }

    static WeaponFinish Wobble(this WeaponFinish f, float amount, float speed)
    {
        f.wobble = amount;
        f.wobbleSpeed = speed;
        return f;
    }

    static WeaponFinish Aura(this WeaponFinish f, string sprite, Color colour, float rate, float size, float life,
                             WeaponFinish.Motion motion, bool glows = true, bool rainbow = false)
    {
        f.auraSprite = Load<Texture2D>($"{Sprites}/{sprite}.png");
        f.auraColour = colour;
        f.auraRate = rate;
        f.auraSize = size;
        f.auraLife = life;
        f.auraMotion = motion;
        f.auraGlows = glows;
        f.auraRainbow = rainbow;
        return f;
    }

    static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            Debug.LogError($"[skins] nothing at {path}");
        return asset;
    }

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
}
