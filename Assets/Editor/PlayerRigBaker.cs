using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the gorilla model and its hitboxes directly onto the player prefab.
///
/// Both were built entirely at runtime - MonkeyRig.Build instantiated the model and
/// Hitbox.BuildFor generated every collider fresh, every time a player spawned. Nothing ever
/// existed on the prefab asset itself, so there was nothing in it to select, move or resize.
/// Reported directly: wanted the model and hitboxes on the prefab so they could be adjusted by
/// hand.
///
/// Re-runnable rather than a one-off migration. MonkeyRig.Build and PlayerController.Start both
/// check for this baked content first and reuse it instead of building a second copy on top, so
/// running this again after the model or the hitbox profile changes just refreshes what is
/// already there instead of duplicating it.
/// </summary>
public static class PlayerRigBaker
{
    const string PrefabPath = "Assets/Resources/PhotonPrefabs/PlayerController.prefab";

    [MenuItem("Tools/Gorilla Warfare/Bake the player rig")]
    public static void Run()
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[rig] no prefab at {PrefabPath}");
            Fail();
            return;
        }

        PlayerController controller = prefab.GetComponent<PlayerController>();
        if (controller == null)
        {
            Debug.LogError("[rig] prefab has no PlayerController");
            PrefabUtility.UnloadPrefabContents(prefab);
            Fail();
            return;
        }

        MonkeyRig rig = prefab.GetComponent<MonkeyRig>();
        bool addedRig = rig == null;
        if (rig == null)
            rig = prefab.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Debug.LogError("[rig] MonkeyRig.Build failed - see the error above");
            if (addedRig)
                Object.DestroyImmediate(rig, true);
            PrefabUtility.UnloadPrefabContents(prefab);
            Fail();
            return;
        }

        Hitbox[] existing = prefab.GetComponentsInChildren<Hitbox>(true);
        int boxes = existing.Length;

        if (boxes == 0)
        {
            boxes = Hitbox.BuildFor(prefab.transform, controller);
            if (boxes == 0)
                Debug.LogError("[rig] no hitboxes built - check the HitboxProfile and bone names");
        }

        // Same layer PlayerController.Start() puts every spawned player on - baked in too so
        // the prefab reads correctly (and traces correctly) even before anyone spawns from it.
        int playerLayer = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        if (playerLayer >= 0)
            prefab.layer = playerLayer;

        int weapons = BuildWeaponPreviews(prefab);

        PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        AssetDatabase.SaveAssets();
        PrefabUtility.UnloadPrefabContents(prefab);

        Debug.Log($"[rig] baked - model, {boxes} hitbox(es) and {weapons} weapon preview(s) saved onto the prefab");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// <summary>
    /// One real, correctly anchored model per weapon, sitting on the prefab so a hold rotation
    /// can be judged and hand-tuned by looking straight at it in the Scene view - direct request,
    /// after the melee hold's rotation had to be guessed and re-rendered several times over:
    /// "put the weapons and everything on the player prefab too because its clear that you cant
    /// do a task as simple as turning a 3D model upside down."
    ///
    /// Built through SingleShotGun.BuildVisual itself rather than reimplemented here, so there is
    /// exactly one place that knows how a weapon gets anchored to a hand - a second copy of that
    /// logic is exactly the kind of thing that quietly drifts from the real one.
    ///
    /// Deliberately NOT built inside ItemHolder - ProjectCleanup.cs already treats anything
    /// sitting in that holder in the prefab asset as a leftover to strip, because
    /// WeaponLoadout.Build owns filling it at runtime. A weapon object is built as a temporary
    /// child of the real ItemHolder just long enough for BuildVisual's own grip math to anchor it
    /// correctly, then reparented onto a separate `~WeaponPreviews` group (kept inactive, so nothing
    /// about a real spawn ever sees it) with its SingleShotGun component removed again - what's
    /// left behind is inert geometry, not a second, non-functional gun that could ever fire.
    /// </summary>
    static int BuildWeaponPreviews(GameObject prefab)
    {
        Transform holder = FindChild(prefab.transform, "ItemHolder");
        if (holder == null)
        {
            Debug.LogError("[rig] no ItemHolder - skipping weapon previews");
            return 0;
        }

        Transform previews = FindChild(prefab.transform, "~WeaponPreviews");
        if (previews != null)
            Object.DestroyImmediate(previews.gameObject, true);

        GameObject previewRoot = new GameObject("~WeaponPreviews");
        previewRoot.transform.SetParent(prefab.transform, false);

        int built = 0;

        foreach (string key in WeaponLoadout.Everything)
        {
            GunInfo info = Resources.Load<GunInfo>($"Guns/{key}");
            if (info == null)
            {
                Debug.LogWarning($"[rig] no GunInfo for '{key}' - skipping its preview");
                continue;
            }

            GameObject temp = new GameObject(key);
            temp.transform.SetParent(holder, false);

            SingleShotGun gun = temp.AddComponent<SingleShotGun>();
            gun.Configure(info, null, false);
            gun.BuildVisual();

            // The component did its one job (anchoring the model); keeping it around would mean
            // a second, inert SingleShotGun getting a real Awake() (and a real MuzzleFlash) the
            // moment any player actually spawns from this prefab.
            Object.DestroyImmediate(gun, true);

            temp.transform.SetParent(previewRoot.transform, true);
            built++;
        }

        previewRoot.SetActive(false);
        return built;
    }

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }

        return null;
    }

    static void Fail()
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(1);
    }
}
