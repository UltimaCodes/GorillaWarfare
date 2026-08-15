using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Photon.Pun;

// What everyone else sees of you, and what their copy of you is holding.
//
// Everything else about a weapon is verified against the owner's copy, which is the one the
// checks could reach. But a remote copy takes a completely different path through
// PlayerController.Start - no loadout is built, nothing is configured - so none of that
// carries over. This asserts the parts that only exist on somebody else's screen.
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

        CheckHolderIsEmpty(instance);
        CheckItemsArrayCoversTheLoadout(instance);
        CheckEquipItemBounds(instance);
        CheckPhotonViewCount(instance);

        Object.DestroyImmediate(instance);

        foreach (string note in Notes)
            Debug.Log($"[remote] {note}");

        foreach (string failure in Failures)
            Debug.LogError($"[remote] FAIL {failure}");

        Debug.Log(Failures.Count == 0 ? "[remote] ALL PASS" : $"[remote] {Failures.Count} FAILURES");
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

    // WeaponLoadout.Build clears the holder before it builds, but it only runs for the owner.
    // Anything left parented here in the prefab is what every other client renders on your hand.
    static void CheckHolderIsEmpty(GameObject instance)
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

        Notes.Add($"ItemHolder children: {leftovers.Count} [{string.Join(", ", leftovers)}]");

        if (leftovers.Count > 0)
            Failures.Add($"ItemHolder still carries {leftovers.Count} prefab weapon(s) - remote players render these, not bananas");
    }

    // The owner builds a loadout and stores it in items[]; remote copies never do, so they keep
    // whatever the prefab serialized. itemIndex is replicated between the two.
    static void CheckItemsArrayCoversTheLoadout(GameObject instance)
    {
        PlayerController controller = instance.GetComponent<PlayerController>();
        FieldInfo field = typeof(PlayerController).GetField("items", BindingFlags.NonPublic | BindingFlags.Instance);
        Item[] items = field.GetValue(controller) as Item[];

        int serialized = items != null ? items.Length : 0;
        int loadout = WeaponLoadout.AllWeapons.Length;

        Notes.Add($"prefab items[] length {serialized}, owner loadout length {loadout}");

        if (serialized < loadout)
            Failures.Add($"itemIndex is replicated but a remote copy only has {serialized} items for an owner carrying {loadout}");
    }

    // The replicated itemIndex is fed straight into items[] with no bounds check.
    static void CheckEquipItemBounds(GameObject instance)
    {
        PlayerController controller = instance.GetComponent<PlayerController>();
        MethodInfo equip = typeof(PlayerController).GetMethod("EquipItem", BindingFlags.NonPublic | BindingFlags.Instance);

        int highest = WeaponLoadout.AllWeapons.Length - 1;

        try
        {
            equip.Invoke(controller, new object[] { highest });
            Notes.Add($"EquipItem({highest}) survived");
        }
        catch (TargetInvocationException e)
        {
            Failures.Add($"EquipItem({highest}) threw {e.InnerException.GetType().Name} - this is what a remote client runs when you switch weapons");
        }
    }

    // The loadout rewrite moved shots onto the player's view so weapons wouldn't need their own.
    static void CheckPhotonViewCount(GameObject instance)
    {
        PhotonView[] views = instance.GetComponentsInChildren<PhotonView>(true);
        Notes.Add($"PhotonViews on the prefab: {views.Length}");

        if (views.Length > 1)
            Failures.Add($"{views.Length} PhotonViews per player - every spawn and respawn allocates all of them");
    }
}
