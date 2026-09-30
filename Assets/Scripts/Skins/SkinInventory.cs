using System;
using System.Collections.Generic;
using System.Text;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// The finishes you own and which one each weapon wears.
///
/// Owned finishes stack - "players get copies but they can scrap any skin for tokens" - so this
/// keeps a count per finish, and scrapping takes one copy and pays WeaponFinish.ScrapValue. A
/// finish is universal: owning one copy is enough to put it on every weapon at once.
///
/// Local to this machine, the same as the wallet (PlayerPrefs, see PlayerWallet for why not a
/// Photon property). What each weapon wears is also published as a player property ("skins"), so
/// everyone else draws your weapons in your finishes - set while not in a room, PUN holds it and
/// sends it with the join.
/// </summary>
public static class SkinInventory
{
    public const string PropertyKey = "skins";

    const string DefaultPrefix = "GW_Skins_";
    static string ownedKey = DefaultPrefix + "Owned";
    static string equippedKey = DefaultPrefix + "Equipped";

    static Dictionary<string, int> owned;
    static Dictionary<string, string> equipped;

    public static event Action Changed;

    /// For batch-mode tests only - see GameSettings.UsePrefsNamespace.
    public static void UsePrefsNamespace(string prefix)
    {
        string root = string.IsNullOrEmpty(prefix) ? DefaultPrefix : prefix;
        ownedKey = root + "Owned";
        equippedKey = root + "Equipped";
        owned = null;
        equipped = null;
        Publish();
        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- owning

    public static int Count(WeaponFinish finish) => finish != null ? Count(finish.key) : 0;

    public static int Count(string key)
    {
        Load();
        return key != null && owned.TryGetValue(key, out int count) ? count : 0;
    }

    /// Every finish you have at least one of, in the catalogue's order - rarest last.
    public static List<WeaponFinish> Owned()
    {
        List<WeaponFinish> found = new List<WeaponFinish>();
        foreach (WeaponFinish finish in FinishCatalog.All)
        {
            if (Count(finish) > 0)
                found.Add(finish);
        }

        return found;
    }

    public static int TotalCopies
    {
        get
        {
            Load();
            int total = 0;
            foreach (int count in owned.Values)
                total += count;
            return total;
        }
    }

    /// One more copy. Returns how many you have now - 1 means it's new.
    public static int Grant(WeaponFinish finish)
    {
        if (finish == null)
            return 0;

        Load();
        int count = Count(finish) + 1;
        owned[finish.key] = count;
        Save();
        return count;
    }

    /// Takes one copy and pays for it. The last copy goes too if you ask - and comes off every
    /// weapon wearing it.
    public static bool Scrap(WeaponFinish finish)
    {
        if (finish == null || Count(finish) <= 0)
            return false;

        Load();
        int left = Count(finish) - 1;
        if (left > 0)
            owned[finish.key] = left;
        else
        {
            owned.Remove(finish.key);
            foreach (string weapon in new List<string>(equipped.Keys))
            {
                if (equipped[weapon] == finish.key)
                    equipped.Remove(weapon);
            }
        }

        Save();
        PlayerWallet.Add(finish.ScrapValue);
        return true;
    }

    /// Every copy past the first of everything, scrapped at once. Returns the tokens it paid.
    public static int ScrapSpares()
    {
        int paid = 0;
        foreach (WeaponFinish finish in FinishCatalog.All)
        {
            while (Count(finish) > 1)
            {
                paid += finish.ScrapValue;
                Scrap(finish);
            }
        }

        return paid;
    }

    // ---------------------------------------------------------------- wearing

    /// What this weapon wears, or null for its stock look.
    public static WeaponFinish Equipped(string weapon)
    {
        Load();
        return weapon != null && equipped.TryGetValue(weapon, out string key) ? FinishCatalog.Find(key) : null;
    }

    /// Puts a finish you own on a weapon. Null takes it back to stock.
    public static void Equip(string weapon, WeaponFinish finish)
    {
        if (string.IsNullOrEmpty(weapon) || (finish != null && Count(finish) <= 0))
            return;

        Load();
        if (finish == null)
            equipped.Remove(weapon);
        else
            equipped[weapon] = finish.key;

        Save();
    }

    /// Every weapon in the game wearing it at once - a finish is universal.
    public static void EquipEverywhere(WeaponFinish finish)
    {
        if (finish != null && Count(finish) <= 0)
            return;

        Load();
        foreach (string weapon in WeaponLoadout.Everything)
        {
            if (finish == null)
                equipped.Remove(weapon);
            else
                equipped[weapon] = finish.key;
        }

        Save();
    }

    // ---------------------------------------------------------------- everyone else's

    /// What a player's weapon wears, as they published it - or your own, for you.
    public static WeaponFinish EquippedBy(Player player, string weapon)
    {
        if (player == null || player.IsLocal)
            return Equipped(weapon);

        if (!player.CustomProperties.TryGetValue(PropertyKey, out object value) || !(value is string encoded))
            return null;

        return Decode(encoded).TryGetValue(weapon, out string key) ? FinishCatalog.Find(key) : null;
    }

    /// Sends what you're wearing to everyone - on every change, and once at start.
    public static void Publish()
    {
        if (PhotonNetwork.LocalPlayer == null)
            return;

        Load();
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { PropertyKey, Encode(equipped) } });
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void PublishAtStart() => Publish();

    static readonly Dictionary<string, Dictionary<string, string>> decoded = new Dictionary<string, Dictionary<string, string>>();

    static string Encode(Dictionary<string, string> map)
    {
        StringBuilder text = new StringBuilder();
        foreach (KeyValuePair<string, string> entry in map)
        {
            if (text.Length > 0)
                text.Append(';');
            text.Append(entry.Key).Append('=').Append(entry.Value);
        }

        return text.ToString();
    }

    static Dictionary<string, string> Decode(string text)
    {
        if (decoded.TryGetValue(text, out Dictionary<string, string> cached))
            return cached;

        Dictionary<string, string> map = new Dictionary<string, string>();
        foreach (string pair in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            int split = pair.IndexOf('=');
            if (split > 0)
                map[pair.Substring(0, split)] = pair.Substring(split + 1);
        }

        if (decoded.Count > 64)
            decoded.Clear();
        decoded[text] = map;
        return map;
    }

    // ---------------------------------------------------------------- saving

    static void Load()
    {
        if (owned != null)
            return;

        owned = new Dictionary<string, int>();
        foreach (KeyValuePair<string, string> entry in Decode(PlayerPrefs.GetString(ownedKey, "")))
        {
            if (int.TryParse(entry.Value, out int count) && count > 0)
                owned[entry.Key] = count;
        }

        equipped = new Dictionary<string, string>(Decode(PlayerPrefs.GetString(equippedKey, "")));
    }

    static void Save()
    {
        Dictionary<string, string> counts = new Dictionary<string, string>();
        foreach (KeyValuePair<string, int> entry in owned)
            counts[entry.Key] = entry.Value.ToString();

        PlayerPrefs.SetString(ownedKey, Encode(counts));
        PlayerPrefs.SetString(equippedKey, Encode(equipped));
        PlayerPrefs.Save();

        Publish();
        Changed?.Invoke();
    }
}
