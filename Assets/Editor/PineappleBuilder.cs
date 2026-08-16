using System.IO;
using UnityEditor;
using UnityEngine;

// Builds the pineapple launcher: the GunInfo, its material, and its place on the roster.
//
// Written as a script rather than clicked together in the inspector for the reason everything
// else here is: the numbers are the design, and a design that lives only in a .asset file is a
// design nobody can read a diff of.
//
// The three numbers that decide whether this weapon is any good are selfKnockback,
// selfDamageScale and armingDistance. They are feel numbers - no check will ever say they are
// wrong, and the only way to find out is to fire it at your own feet.
public static class PineappleBuilder
{
    const string GunPath = "Assets/Resources/Guns/Pineapple.asset";
    const string MaterialPath = "Assets/Resources/Models/Weapons/PineappleMat.mat";
    const string Atlas = "Assets/Art/Jungle/foodColormap.png";

    [MenuItem("Tools/Gorilla Warfare/Build the pineapple launcher")]
    public static void Run()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas);

            // Matte. The whole kit is flat colour on a tiny atlas and a highlight on it reads
            // as wet plastic rather than as fruit.
            material.SetFloat("_Glossiness", 0.08f);
            material.SetFloat("_Metallic", 0f);
            material.enableInstancing = true;

            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        GunInfo gun = AssetDatabase.LoadAssetAtPath<GunInfo>(GunPath);
        bool making = gun == null;

        if (making)
            gun = ScriptableObject.CreateInstance<GunInfo>();

        gun.itemName = "The Grenada";

        // ---- what it does when it lands ----
        gun.projectile = true;
        gun.damage = 105f;
        gun.blastRadius = 5f;
        gun.projectileSpeed = 34f;
        gun.projectileGravity = 0.55f;

        // Far enough that firing at the floor under you launches you rather than killing you,
        // close enough that it still goes off on somebody standing in a doorway.
        gun.armingDistance = 1.2f;

        // ---- what it does to whoever fired it ----
        //
        // No self damage and a big shove. TF2 charges health for a rocket jump because TF2 has
        // healers and a twelve player economy; five friends in a deathmatch have neither, and
        // "I blew myself onto the roof and arrived on thirty health" is a worse story than "I
        // blew myself onto the roof". The cost is commitment - you go where the blast sends you
        // and you cannot steer much on the way.
        gun.selfDamageScale = 0f;
        gun.selfKnockback = 15f;
        gun.knockback = 9f;

        // ---- what it costs ----
        //
        // A hundred and five damage inside five metres would be oppressive on any sensible fire
        // rate, so it pays in every other currency: barely one shot a second, four in the tube,
        // and a travel time long enough to walk out of.
        gun.fireRate = 0.9f;
        gun.automatic = false;
        gun.magazineSize = 4;
        gun.spareMagazines = 4;
        gun.reloadTime = 2.6f;
        gun.pelletsPerShot = 1;
        gun.spread = 0f;
        gun.maxRange = 120f;
        gun.falloffStart = 120f;
        gun.falloffFloor = 1f;

        gun.canAim = false;
        gun.twoHanded = true;
        gun.melee = false;

        // Ripeness is the ammo readout on the model itself. A pineapple does not brown the way
        // a banana does, so these run green to gold rather than green to brown.
        gun.unripe = new Color(0.42f, 0.78f, 0.28f);
        gun.ripe = new Color(0.96f, 0.78f, 0.22f);
        gun.overripe = new Color(0.72f, 0.45f, 0.13f);

        if (making)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GunPath));
            AssetDatabase.CreateAsset(gun, GunPath);
        }

        EditorUtility.SetDirty(gun);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[pineapple] {(making ? "created" : "updated")} {GunPath} - "
                  + $"{gun.damage} in a {gun.blastRadius}m blast at {gun.fireRate}/s, "
                  + $"self knockback {gun.selfKnockback}, self damage {gun.selfDamageScale:P0}");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
