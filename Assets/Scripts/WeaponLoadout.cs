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
    /// Clears whatever is in the holder and builds the named weapons into it.
    /// Returns the weapons in the order requested.
    /// </summary>
    public List<SingleShotGun> Build(Transform holder, Camera cam, IEnumerable<string> weaponNames)
    {
        built.Clear();

        if (holder == null)
        {
            Debug.LogError("[loadout] no item holder to build into.", this);
            return built;
        }

        // Anything already parented here came from the prefab and is being replaced.
        for (int i = holder.childCount - 1; i >= 0; i--)
            Destroy(holder.GetChild(i).gameObject);

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
            gun.Configure(info, cam, impact);

            built.Add(gun);
        }

        // Only the first is drawn; the rest wait to be switched to.
        for (int i = 0; i < built.Count; i++)
            built[i].gameObject.SetActive(i == 0);

        return built;
    }

    /// A random selection, used by deathmatch. Never returns duplicates.
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
