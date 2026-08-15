using UnityEditor;
using UnityEngine;

// Names the weapons, and says which ones need both hands.
//
// The asset filenames stay as roles - Pistol, Shotgun, Rifle, Sniper, Peel - because the gun
// game ladder is defined in power order and "Pistol -> Shotgun -> Rifle -> Sniper -> Peel" tells
// you what it does at a glance in a way that a list of cultivars would not. What players see
// lives in itemName on the asset, which is what the HUD, the kill feed and the loadout reveal
// all read.
//
// Written as a script rather than typed into five inspectors so the whole set can be seen at
// once, and so re-running it puts anything back that gets nudged.
public static class WeaponNaming
{
    // key, name on screen, needs both hands, aims down sights
    static readonly (string key, string name, bool twoHanded, bool canAim)[] Weapons =
    {
        // The supermarket banana. Ordinary, dependable, the one everyone starts with.
        ("Pistol",  "Cavendish",    false, false),

        // Two bananas taped side by side, and a banana split is two halves in one dish.
        ("Shotgun", "The Split",     true,  false),

        // A bunch is a lot of bananas at once, which is also what this does.
        ("Rifle",   "The Bunch",     true,  false),

        // Gros Michel, the cultivar that was wiped out in the fifties, known as Big Mike. It was
        // also longer than a Cavendish, which suits the absurd one.
        ("Sniper",  "Big Mike",      true,  true),

        // What's left after you eat one, and what everyone does about it.
        ("Peel",    "Slip Hazard",  false, false),
    };

    [MenuItem("Tools/Gorilla Warfare/Name the weapons")]
    public static void Run()
    {
        int changed = 0;
        int missing = 0;

        foreach ((string key, string name, bool twoHanded, bool canAim) in Weapons)
        {
            string path = $"Assets/Resources/Guns/{key}.asset";
            GunInfo info = AssetDatabase.LoadAssetAtPath<GunInfo>(path);

            if (info == null)
            {
                Debug.LogError($"[names] no gun asset at {path}");
                missing++;
                continue;
            }

            bool dirty = info.itemName != name || info.twoHanded != twoHanded || info.canAim != canAim;

            info.itemName = name;
            info.twoHanded = twoHanded;
            info.canAim = canAim;

            if (dirty)
            {
                EditorUtility.SetDirty(info);
                changed++;
            }

            Debug.Log($"[names] {key,-8} -> {name,-14} {(twoHanded ? "two handed" : "one handed")}"
                      + (canAim ? $", aims at {info.aimFov:F0} fov" : ""));
        }

        AssetDatabase.SaveAssets();

        Debug.Log(missing == 0
            ? $"[names] {Weapons.Length} weapons named, {changed} changed"
            : $"[names] {missing} MISSING");

        if (Application.isBatchMode)
            EditorApplication.Exit(missing == 0 ? 0 : 1);
    }
}
