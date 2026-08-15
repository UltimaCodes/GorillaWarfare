using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Photon.Pun;

// What everyone else sees of you, and what their copy of you is holding.
//
// The weapon and model checks only ever exercised the owner's copy, because that's the one that
// gets a loadout built. A remote copy takes a different branch through PlayerController.Start,
// so none of what those suites prove carries over. This covers the other branch, plus the
// handful of invariants that hold the two together.
public static class RemoteCopyCheck
{
    const string PrefabPath = "Assets/Resources/PhotonPrefabs/PlayerController.prefab";

    static readonly List<string> Failures = new List<string>();
    static readonly List<string> Notes = new List<string>();

    public static void Run()
    {
        Failures.Clear();
        Notes.Clear();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[remote] no prefab at {PrefabPath}");
            EditorApplication.Exit(1);
            return;
        }

        GameObject instance = Object.Instantiate(prefab);

        CheckHolderShipsEmpty(instance);
        CheckPhotonViewCount(instance);
        CheckEquipItemSurvivesABadIndex(instance);

        Object.DestroyImmediate(instance);

        CheckLoadoutClearingSparesTheArms();
        CheckLoadoutFallback();
        CheckLadderIsBuildable();

        foreach (string note in Notes)
            Debug.Log($"[remote] {note}");

        foreach (string failure in Failures)
            Debug.LogError($"[remote] FAIL {failure}");

        Debug.Log(Failures.Count == 0 ? "[remote] ===== ALL PASS =====" : $"[remote] {Failures.Count} FAILURES");
        EditorApplication.Exit(Failures.Count == 0 ? 0 : 1);
    }

    static Transform FindItemHolder(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ItemHolder")
                return t;
        }

        return null;
    }

    // Weapons are built at runtime on every copy. Anything sitting in the holder in the asset
    // is a leftover, and it's a leftover other people can see hanging off your hand.
    static void CheckHolderShipsEmpty(GameObject instance)
    {
        Transform holder = FindItemHolder(instance);
        if (holder == null)
        {
            Failures.Add("no ItemHolder on the prefab");
            return;
        }

        List<string> leftovers = new List<string>();
        foreach (Transform child in holder)
            leftovers.Add(child.name);

        Notes.Add($"ItemHolder ships with {leftovers.Count} children");

        if (leftovers.Count > 0)
            Failures.Add($"ItemHolder still carries [{string.Join(", ", leftovers)}] - remote players render these");
    }

    // Runtime loadouts only work because shots report through the player's own view.
    static void CheckPhotonViewCount(GameObject instance)
    {
        PhotonView[] views = instance.GetComponentsInChildren<PhotonView>(true);
        Notes.Add($"PhotonViews per player: {views.Length}");

        if (views.Length != 1)
            Failures.Add($"{views.Length} PhotonViews per player - every spawn and respawn allocates all of them");
    }

    // itemIndex arrives over the network and there is no guarantee the sender's loadout and the
    // receiver's are the same size yet. It threw for months; it must now be survivable.
    static void CheckEquipItemSurvivesABadIndex(GameObject instance)
    {
        PlayerController controller = instance.GetComponent<PlayerController>();
        MethodInfo equip = typeof(PlayerController).GetMethod("EquipItem", BindingFlags.NonPublic | BindingFlags.Instance);

        if (equip == null)
        {
            Failures.Add("no EquipItem to test");
            return;
        }

        foreach (int index in new[] { -1, 0, 3, 99 })
        {
            try
            {
                equip.Invoke(controller, new object[] { index });
            }
            catch (TargetInvocationException e)
            {
                Failures.Add($"EquipItem({index}) threw {e.InnerException.GetType().Name} - a remote client runs this on every weapon switch");
                return;
            }
        }

        Notes.Add("EquipItem survives -1, 0, 3 and 99 against an unbuilt loadout");
    }

    // The first person arms live in the same holder the loadout builds into. Clearing it
    // wholesale destroyed them one frame after spawn, which is why the hands were never
    // visible however carefully they were positioned.
    static void CheckLoadoutClearingSparesTheArms()
    {
        GameObject host = new GameObject("loadout host");
        GameObject holder = new GameObject("ItemHolder");
        holder.transform.SetParent(host.transform, false);

        GameObject arms = new GameObject("ViewArms");
        arms.transform.SetParent(holder.transform, false);

        WeaponLoadout loadout = host.AddComponent<WeaponLoadout>();
        loadout.Build(holder.transform, null, WeaponLoadout.AllWeapons, false);

        bool armsSurvived = arms != null && arms.transform.parent == holder.transform;
        Notes.Add($"arms survive a loadout build: {armsSurvived}");

        if (!armsSurvived)
            Failures.Add("building a loadout destroys the view arms - the hands will vanish one frame after spawning");

        int weapons = 0;
        foreach (Transform child in holder.transform)
        {
            if (child.GetComponent<Item>() != null)
                weapons++;
        }

        if (weapons != WeaponLoadout.AllWeapons.Length)
            Failures.Add($"built {weapons} weapons out of {WeaponLoadout.AllWeapons.Length}");

        Object.DestroyImmediate(host);
    }

    // A client can receive a player before it receives that player's loadout property. The
    // fallback has to be something buildable, not an empty array.
    static void CheckLoadoutFallback()
    {
        string[] fallback = PlayerController.LoadoutFor(null);

        Notes.Add($"loadout fallback: [{string.Join(", ", fallback)}]");

        if (fallback == null || fallback.Length == 0)
            Failures.Add("LoadoutFor falls back to nothing - a player with no property yet would spawn unarmed");
    }

    // Gun game walks this list and hands out one weapon at a time, so every rung has to resolve
    // to a real asset or somebody climbs into an empty hand.
    static void CheckLadderIsBuildable()
    {
        foreach (string weapon in WeaponLoadout.GunGameLadder)
        {
            GunInfo info = Resources.Load<GunInfo>(WeaponLoadout.GunResourcePath + weapon);

            if (info == null)
            {
                Failures.Add($"gun game ladder wants '{weapon}' and there is no asset for it");
                continue;
            }

            // Keys stay as roles; what players read comes off the asset. A weapon added without
            // one would show its key on the HUD and in the kill feed.
            if (string.IsNullOrWhiteSpace(info.itemName) || info.itemName == weapon)
                Failures.Add($"'{weapon}' has no name of its own - run WeaponNaming");
        }

        string[] shown = new string[WeaponLoadout.GunGameLadder.Length];
        for (int i = 0; i < shown.Length; i++)
            shown[i] = WeaponLoadout.DisplayName(WeaponLoadout.GunGameLadder[i]);

        Notes.Add($"gun game ladder: {string.Join(" -> ", shown)}");
    }
}
