using System.Collections.Generic;
using UnityEngine;

// Builds a player's weapons at runtime from a list of names.
//
// They used to live on the player prefab, which meant adding a gun was prefab surgery and a
// gamemode couldn't change what you were carrying. Now a loadout is just a string array, which
// is what the deathmatch's random three-weapon roll and gun game's ladder both need.
//
// This only works because weapons no longer carry their own PhotonView - shots are reported
// through the player's. Allocating view IDs for runtime-spawned objects is a mess.
public class WeaponLoadout : MonoBehaviour
{
    public const string GunResourcePath = "Guns/";

    /// What you get when a loadout resolves to nothing at all. The pistol, because it's the
    /// weapon the game already assumes everyone can use - it's rung one of the ladder and the
    /// thing a fresh player starts with.
    public const string Fallback = "Pistol";

    /// Every weapon in the game, in power order. Gun game walks this list; deathmatch samples it.
    /// <summary>
    /// Every shooting weapon, as a fresh array each time.
    ///
    /// A property rather than a readonly field, and this is not fussiness. It used to hand out
    /// the one shared array, PlayerController.LoadoutFor returned it directly as its fallback,
    /// and the HUD's warmup reveal wrote display names into it in place - so the moment that
    /// reveal ran before the loadout property arrived, the game's master list of weapons
    /// permanently became { CAVENDISH, THE SPLIT, THE BUNCH, BIG MIKE }. Every Resources.Load
    /// after that failed, nobody could switch weapons, and it stayed broken until the domain
    /// reloaded, because statics outlive a match.
    ///
    /// readonly on an array only stops you reassigning the variable. It says nothing whatsoever
    /// about the contents.
    /// </summary>
    public static string[] AllWeapons => (string[])allWeapons.Clone();

    static readonly string[] allWeapons = { "Pistol", "Shotgun", "Rifle", "Sniper", "Pineapple" };

    /// Gun game order - weakest first, melee last. Killing with the peel wins the match.
    // The launcher sits second from the top: harder than everything before it and a genuine
    // reward for getting there, but still not the peel, because ending a gun game on anything
    // other than beating somebody to death with rubbish would be a waste.
    /// <summary>
    /// Literally everything, melee included. Only the sandbox uses this.
    ///
    /// Built from the ladder rather than listed again, so a weapon added to the game turns up
    /// here without anybody remembering to add it - which is exactly the sort of thing nobody
    /// remembers.
    /// </summary>
    public static string[] Everything => (string[])GunGameLadder.Clone();

    public static readonly string[] GunGameLadder =
        { "Pistol", "Shotgun", "Rifle", "Sniper", "Pineapple", "Peel" };

    // Resolved from the asset the first time anything asks, then kept. The HUD asks every
    // frame and Resources.Load is not free.
    static readonly Dictionary<string, string> displayNames = new Dictionary<string, string>();

    /// <summary>
    /// What a weapon is called on screen. The keys stay as roles - Pistol, Rifle, Sniper - so
    /// the ladder still reads in power order and anyone opening the code can tell what a weapon
    /// is for. The names players see live on the asset.
    /// </summary>
    public static string DisplayName(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        if (displayNames.TryGetValue(key, out string name))
            return name;

        GunInfo info = Resources.Load<GunInfo>(GunResourcePath + key);
        name = info != null && !string.IsNullOrWhiteSpace(info.itemName) ? info.itemName : key;

        displayNames[key] = name;
        return name;
    }

    readonly List<SingleShotGun> built = new List<SingleShotGun>();

    public IReadOnlyList<SingleShotGun> Weapons => built;

    /// <summary>
    /// Clears whatever weapons are in the holder and builds the named ones into it.
    /// Returns them in the order requested.
    /// </summary>
    /// <param name="owned">
    /// False for the copies of a player that other people see. Those still need the models -
    /// that's how anyone can tell what you're holding - but they must never trace a shot.
    /// </param>
    void Spawn(Transform holder, Camera cam, bool owned, string name)
    {
        GunInfo info = Resources.Load<GunInfo>(GunResourcePath + name);

        if (info == null)
        {
            Debug.LogError($"[loadout] no gun asset at Resources/{GunResourcePath}{name}", this);
            return;
        }

        // Name matters: the fire RPC finds the weapon by it, and the audio bank and banana
        // model are both looked up from it.
        GameObject go = new GameObject(name);
        go.transform.SetParent(holder, false);

        SingleShotGun gun = go.AddComponent<SingleShotGun>();
        gun.Configure(info, cam, owned);

        built.Add(gun);
    }

    public List<SingleShotGun> Build(Transform holder, Camera cam, IEnumerable<string> weaponNames, bool owned)
    {
        built.Clear();

        if (holder == null)
        {
            Debug.LogError("[loadout] no item holder to build into.", this);
            return built;
        }

        // Only clear out weapons. The holder is also where the first person arms live, and
        // emptying it wholesale used to delete those the moment a loadout was built - they
        // were being destroyed one frame after spawning, which is why nobody ever saw a hand.
        for (int i = holder.childCount - 1; i >= 0; i--)
        {
            Transform child = holder.GetChild(i);
            if (child.GetComponent<Item>() != null)
                Destroy(child.gameObject);
        }

        foreach (string name in weaponNames)
            Spawn(holder, cam, owned, name);

        // Nobody stands there empty handed. Whatever went wrong upstream - a loadout property
        // carried in from another room, a weapon renamed out from under a saved one, a mode
        // that never issued you anything - the symptom is joining a match with nothing to
        // shoot with, and that is the worst possible way to find out about a bug.
        if (built.Count == 0)
        {
            Debug.LogError($"[loadout] nothing resolved from [{string.Join(", ", weaponNames)}], "
                           + $"falling back to {Fallback}", this);

            Spawn(holder, cam, owned, Fallback);
        }

        // Only the first is drawn; the rest wait to be switched to.
        for (int i = 0; i < built.Count; i++)
            built[i].gameObject.SetActive(i == 0);

        return built;
    }

    /// A random selection, used by deathmatch. Never returns duplicates, never returns melee.
    public static string[] RandomSelection(int count)
    {
        List<string> pool = new List<string>(AllWeapons);
        List<string> picked = new List<string>();

        count = Mathf.Clamp(count, 1, pool.Count);
        for (int i = 0; i < count; i++)
        {
            int index = Random.Range(0, pool.Count);
            picked.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return picked.ToArray();
    }
}
