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
    public const string ImpactResource = "Prefabs/BulletImpact";

    /// Every weapon in the game, in power order. Gun game walks this list; deathmatch samples it.
    public static readonly string[] AllWeapons = { "Pistol", "Shotgun", "Rifle", "Sniper" };

    /// Gun game order - weakest first, melee last. Killing with the peel wins the match.
    public static readonly string[] GunGameLadder = { "Pistol", "Shotgun", "Rifle", "Sniper", "Peel" };

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

        GameObject impact = Resources.Load<GameObject>(ImpactResource);

        foreach (string name in weaponNames)
        {
            GunInfo info = Resources.Load<GunInfo>(GunResourcePath + name);
            if (info == null)
            {
                Debug.LogError($"[loadout] no gun asset at Resources/{GunResourcePath}{name}", this);
                continue;
            }

            // Name matters: the fire RPC finds the weapon by it, and the audio bank and banana
            // model are both looked up from it.
            GameObject go = new GameObject(name);
            go.transform.SetParent(holder, false);

            SingleShotGun gun = go.AddComponent<SingleShotGun>();
            gun.Configure(info, cam, impact, owned);

            built.Add(gun);
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
