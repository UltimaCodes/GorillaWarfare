using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Purple Haze (`Gatling`) and Red Hot Chili Pepper (`Flamer`): the grapes and the chili from
/// Kenney's Food Kit (CC0, where the Pineapple came from) imported the way the Pineapple is, a
/// material each copied from the Pineapple's (the kit shares one colour map), and the two gun
/// assets with the first-pass numbers from docs/ideas.md.
///
/// Only fills in what's missing - an existing gun asset keeps whatever it's been tuned to, an
/// existing material keeps its edits - so it's safe to run again. The model import settings are
/// the exception: they're set every time, since a reimport can reset them.
///
/// Logs each model's bounds, which is how their turn and size into the hand were picked.
/// </summary>
public static class FoodKitWeaponSetup
{
    const string Folder = "Assets/Resources/Models/Weapons/";
    const string GunFolder = "Assets/Resources/Guns/";

    [MenuItem("Tools/Gorilla Warfare/Set up Purple Haze and Red Hot Chili Pepper")]
    public static void Run()
    {
        bool ok = true;

        foreach (string key in new[] { "Gatling", "Flamer" })
        {
            ok &= ConfigureModel(key);
            ok &= EnsureMaterial(key);
            LogBounds(key);
        }

        EnsureGun("Gatling", Gatling);
        EnsureGun("Flamer", Flamer);

        AssetDatabase.SaveAssets();
        Debug.Log(ok ? "[foodkit] done" : "[foodkit] FAILED - see above");

        if (Application.isBatchMode)
            EditorApplication.Exit(ok ? 0 : 1);
    }

    /// The Pineapple's import: file scale, no animation, cameras or lights, materials left in the
    /// model - the game swaps in `{key}Mat` anyway.
    static bool ConfigureModel(string key)
    {
        string path = Folder + key + ".fbx";
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;

        if (importer == null)
        {
            Debug.LogError($"[foodkit] no model at {path}");
            return false;
        }

        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.bakeAxisConversion = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
        return true;
    }

    static bool EnsureMaterial(string key)
    {
        string path = Folder + key + "Mat.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            return true;

        if (!AssetDatabase.CopyAsset(Folder + "PineappleMat.mat", path))
        {
            Debug.LogError($"[foodkit] couldn't copy PineappleMat to {path}");
            return false;
        }

        AssetDatabase.ImportAsset(path);
        return true;
    }

    static void LogBounds(string key)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + key + ".fbx");
        if (model == null)
            return;

        StringBuilder report = new StringBuilder($"[foodkit] {key}: ");
        Bounds all = new Bounds();
        bool started = false;

        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Bounds b = filter.sharedMesh.bounds;
            report.Append($"\n  mesh '{filter.name}' local {b.size} centre {b.center}, "
                          + $"node rot {filter.transform.localEulerAngles} scale {filter.transform.lossyScale}");

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1,
                                                                                 (i & 2) == 0 ? -1 : 1,
                                                                                 (i & 4) == 0 ? -1 : 1));
                Vector3 inModel = model.transform.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!started)
                {
                    all = new Bounds(inModel, Vector3.zero);
                    started = true;
                }
                else
                {
                    all.Encapsulate(inModel);
                }
            }
        }

        report.Append($"\n  whole model {all.size} centre {all.center}");
        Debug.Log(report.ToString());
    }

    static void EnsureGun(string key, System.Action<GunInfo> fill)
    {
        string path = GunFolder + key + ".asset";
        if (AssetDatabase.LoadAssetAtPath<GunInfo>(path) != null)
        {
            Debug.Log($"[foodkit] {path} already there, left as it is");
            return;
        }

        GunInfo gun = ScriptableObject.CreateInstance<GunInfo>();
        fill(gun);
        AssetDatabase.CreateAsset(gun, path);
        Debug.Log($"[foodkit] made {path} ({gun.itemName})");
    }

    // The numbers are docs/ideas.md's first pass.

    static void Gatling(GunInfo gun)
    {
        gun.itemName = "Purple Haze";

        // 11 x 14 = 154 DPS, a little under the Bunch's 178 - it trades raw damage for the
        // magazine and a lane it can hold, and every grape still has to reach you.
        gun.damage = 11f;
        gun.fireRate = 14f;
        gun.automatic = true;
        gun.maxRange = 120f;
        gun.falloffStart = 20f;
        gun.falloffFloor = 0.6f;

        gun.spread = 0.4f;
        gun.spreadMax = 2.5f;
        gun.spreadGrowSeconds = 3f;

        gun.pelletProjectile = true;
        gun.projectileSpeed = 55f;
        gun.projectileGravity = 0.25f;

        gun.spinUp = 0.6f;
        gun.spinMoveMultiplier = 0.6f;

        gun.twoHanded = true;
        gun.canAim = false;   // aim spins the barrel instead
        gun.muzzleFlash = true;
        gun.reticle = GunInfo.Reticle.Cross;
        gun.ripens = false;

        gun.magazineSize = 120;
        gun.spareMagazines = 1;
        gun.reloadTime = 4f;

        gun.verticalKick = 0.35f;
        gun.horizontalKick = 0.3f;
        gun.patternLength = 20;
        gun.recoilRecovery = 0.5f;
        gun.recoverySpeed = 6f;
        gun.viewKick = 0.35f;   // 14 a second at full kick shoves it into your face

        // The bunch stands up in the kit, stem on top; turned so the stem's in your hand and the
        // bunch points at the enemy, and sized up from life size. Picked from side-on renders.
        gun.modelRotation = new Vector3(-90f, 0f, 0f);
        gun.modelScale = 1.4f;
    }

    static void Flamer(GunInfo gun)
    {
        gun.itemName = "Red Hot Chili Pepper";

        // 25 puffs a second, each touching you once: 4.4 x 25 = 110 DPS point blank, 40% of that
        // at the tip. The magazine is the fuel tank - one puff a unit, 8 seconds of flame.
        gun.damage = 4.4f;
        gun.fireRate = 25f;
        gun.automatic = true;
        gun.maxRange = 8f;
        gun.falloffStart = 8f;
        gun.falloffFloor = 1f;
        gun.spread = 0f;

        gun.flame = true;
        gun.flameSpeed = 14f;
        gun.flameLife = 0.5f;
        gun.flameRadiusStart = 0.2f;
        gun.flameRadiusEnd = 0.8f;
        gun.flameTipDamage = 0.4f;
        gun.burnPerSecond = 8f;
        gun.burnSeconds = 4f;

        gun.airblastKnockback = 12f;
        gun.airblastCost = 20;
        gun.airblastCooldown = 0.75f;
        gun.airblastRange = 5f;

        gun.twoHanded = true;
        gun.canAim = false;   // aim is the airblast
        gun.muzzleFlash = false;
        gun.reticle = GunInfo.Reticle.Cross;
        gun.ripens = false;

        gun.magazineSize = 200;
        gun.spareMagazines = 5;
        gun.reloadTime = 3f;

        gun.verticalKick = 0f;
        gun.horizontalKick = 0f;
        gun.recoilRecovery = 0f;
        gun.viewKick = 0f;   // a stream, not a string of shots

        // The kit's chili lies along Z stem first; turned round so the stem's in your hand and
        // the tip is the nozzle.
        gun.modelRotation = new Vector3(0f, 180f, 0f);
        gun.modelScale = 1.8f;
    }
}
