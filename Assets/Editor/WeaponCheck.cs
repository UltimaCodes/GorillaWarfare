using System.Text;
using UnityEditor;
using UnityEngine;

// Checks the weapon data and the recoil maths without needing play mode.
//
// The recoil pattern is the interesting part: it has to be deterministic (so it can be learned),
// climb early (so there's something to pull against), and flatten later (so it doesn't walk off
// the top of the screen). Those are testable properties, not opinions.
//
// Unity -batchmode -quit -executeMethod WeaponCheck.Run
public static class WeaponCheck
{
    static int failures;

    static void Check(StringBuilder sb, bool ok, string label, string detail)
    {
        if (!ok) failures++;
        sb.AppendLine($"[gun] {(ok ? "PASS" : "FAIL")}  {label,-30} {detail}");
    }

    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        failures = 0;

        GunInfo pistol = AssetDatabase.LoadAssetAtPath<GunInfo>("Assets/Resources/Guns/Pistol.asset");
        GunInfo rifle = AssetDatabase.LoadAssetAtPath<GunInfo>("Assets/Resources/Guns/Rifle.asset");

        Check(sb, pistol != null, "pistol asset", pistol == null ? "missing" : pistol.name);
        Check(sb, rifle != null, "rifle asset", rifle == null ? "missing" : rifle.name);
        if (pistol == null || rifle == null) { Finish(sb); return; }

        Check(sb, !pistol.automatic, "pistol is semi auto", $"automatic={pistol.automatic}");
        Check(sb, rifle.automatic, "rifle is automatic", $"automatic={rifle.automatic}");
        Check(sb, pistol.damage > rifle.damage, "pistol hits harder per shot",
              $"{pistol.damage} vs {rifle.damage}");
        Check(sb, rifle.fireRate > pistol.fireRate, "rifle fires faster",
              $"{rifle.fireRate}/s vs {pistol.fireRate}/s");

        // DPS should favour the rifle, otherwise the semi auto is just better and nobody picks it
        float pistolDps = pistol.damage * pistol.fireRate;
        float rifleDps = rifle.damage * rifle.fireRate;
        Check(sb, rifleDps > pistolDps, "rifle wins on sustained dps",
              $"rifle {rifleDps:F0} vs pistol {pistolDps:F0}");

        // shots to kill against 100 health
        int pistolStk = Mathf.CeilToInt(100f / pistol.damage);
        int rifleStk = Mathf.CeilToInt(100f / rifle.damage);
        Check(sb, pistolStk <= 4 && rifleStk <= 6, "shots to kill are sane",
              $"pistol {pistolStk}, rifle {rifleStk}");

        Check(sb, pistol.magazineSize > 0 && rifle.magazineSize > 0, "magazines set",
              $"pistol {pistol.magazineSize}, rifle {rifle.magazineSize}");

        // ---- recoil pattern ----
        Vector2 a1 = rifle.RecoilForShot(0);
        Vector2 a2 = rifle.RecoilForShot(0);
        Check(sb, a1 == a2, "pattern is deterministic", "same shot index gives the same kick");

        Vector2 early = rifle.RecoilForShot(1);
        Vector2 late = rifle.RecoilForShot(rifle.patternLength * 2);
        Check(sb, early.x > late.x, "climb tapers off",
              $"shot 1 pitch {early.x:F2} > late pitch {late.x:F2}");

        // total climb over a full magazine shouldn't be absurd
        float totalPitch = 0f;
        float maxYaw = 0f;
        for (int i = 0; i < rifle.magazineSize; i++)
        {
            Vector2 k = rifle.RecoilForShot(i);
            totalPitch += k.x;
            maxYaw = Mathf.Max(maxYaw, Mathf.Abs(k.y));
        }
        Check(sb, totalPitch > 10f && totalPitch < 90f, "full mag climb is controllable",
              $"{totalPitch:F0} degrees over {rifle.magazineSize} rounds");
        Check(sb, maxYaw > 0.01f, "pattern drifts sideways", $"max yaw {maxYaw:F2} degrees");

        // early shots should be mostly vertical - sideways drift arrives later
        Check(sb, Mathf.Abs(rifle.RecoilForShot(0).y) < rifle.RecoilForShot(0).x,
              "first shot is near vertical",
              $"yaw {rifle.RecoilForShot(0).y:F2} vs pitch {rifle.RecoilForShot(0).x:F2}");

        // ---- weapon order on the player ----
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/PhotonPrefabs/PlayerController.prefab");
        if (prefab != null)
        {
            Item[] items = prefab.GetComponentsInChildren<Item>(true);
            SerializedObject so = new SerializedObject(prefab.GetComponent<PlayerController>());
            SerializedProperty arr = so.FindProperty("items");
            if (arr != null && arr.arraySize > 0)
            {
                Object first = arr.GetArrayElementAtIndex(0).objectReferenceValue;
                string firstName = first != null ? first.name : "null";
                Check(sb, firstName.ToLower().Contains("pistol"), "pistol is the default weapon",
                      $"slot 0 = {firstName}");
            }
        }

        // ---- banana models actually replace the old guns ----
        foreach (string weapon in new[] { "Pistol", "Rifle" })
        {
            GameObject banana = Resources.Load<GameObject>($"Models/Weapons/Banana{weapon}");
            Check(sb, banana != null, $"banana{weapon} model", banana == null ? "missing" : "loaded");

            if (banana != null)
            {
                MeshFilter mf = banana.GetComponentInChildren<MeshFilter>(true);
                Bounds b = mf != null ? mf.sharedMesh.bounds : new Bounds();

                // Measure the longest axis rather than assuming which one, then separately
                // assert it's the forward one. Assuming the axis is how the last check
                // reported a correctly sized banana as too small.
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                Check(sb, longest > 0.15f && longest < 0.9f, $"banana{weapon} is weapon sized",
                      $"longest axis {longest:F2}m");
                Check(sb, b.size.z >= longest - 0.001f, $"banana{weapon} points forward",
                      $"z {b.size.z:F2} should be the longest of ({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})");
            }
        }

        Material bm = Resources.Load<Material>("Models/Weapons/BananaMat");
        Check(sb, bm != null, "banana material", bm == null ? "missing" : $"{bm.name} {bm.color}");
        Check(sb, bm != null && bm.color.r > 0.7f && bm.color.g > 0.6f && bm.color.b < 0.4f,
              "banana is yellow", bm == null ? "-" : bm.color.ToString());

        // ---- feel components actually attach and do something ----
        GameObject rig = new GameObject("~gunfeel");
        MuzzleFlash mf2 = rig.AddComponent<MuzzleFlash>();
        mf2.Build();
        Light flashLight = rig.GetComponentInChildren<Light>(true);
        Check(sb, flashLight != null, "muzzle flash light built",
              flashLight == null ? "none" : $"{flashLight.type}, range {flashLight.range}");
        Check(sb, flashLight != null && !flashLight.enabled, "flash starts off",
              flashLight == null ? "-" : $"enabled={flashLight.enabled}");
        if (flashLight != null)
        {
            mf2.Fire();
            Check(sb, flashLight.enabled, "firing lights it", $"enabled={flashLight.enabled}");
            Check(sb, flashLight.transform.localPosition.z > 0.1f, "flash sits at the muzzle",
                  $"local z {flashLight.transform.localPosition.z:F2}");
        }
        Object.DestroyImmediate(rig);

        // sway must only move the holder, never the aim
        GameObject holder = new GameObject("~holder");
        holder.transform.localPosition = new Vector3(0.2f, -0.15f, 0.4f);
        Vector3 before = holder.transform.localPosition;
        WeaponSway sway = holder.AddComponent<WeaponSway>();
        sway.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        Check(sb, holder.transform.localPosition == before, "sway rests where it started",
              $"{holder.transform.localPosition} vs {before}");
        Object.DestroyImmediate(holder);

        // ---- the whole roster ----
        sb.AppendLine("[gun] ---------- roster ----------");
        foreach (string name in WeaponLoadout.GunGameLadder)
        {
            GunInfo g = Resources.Load<GunInfo>(WeaponLoadout.GunResourcePath + name);
            Check(sb, g != null, $"{name} asset loads", g == null ? "missing" : "ok");
            if (g == null) continue;

            float burst = g.damage * Mathf.Max(1, g.pelletsPerShot);
            sb.AppendLine($"[gun]       {name,-9} {burst,5:F0} per pull, {g.fireRate,4:F1}/s, " +
                          $"range {g.maxRange,5:F0}, {(g.melee ? "melee" : g.automatic ? "auto " : "semi ")}");

            Check(sb, Resources.Load<GameObject>($"Models/Weapons/Banana{name}") != null,
                  $"{name} has a banana", "model present");

            // Nothing should one-pull a full-health player except the sniper, which pays for it
            // in fire rate, and the shotgun at point blank, which pays for it in range.
            bool onePull = burst >= 100f;
            Check(sb, !onePull || g.fireRate <= 1.5f, $"{name} one-shot is paid for",
                  $"{burst:F0} damage at {g.fireRate:F1}/s");
        }

        // Every weapon needs a reason to exist: no two may share a role.
        GunInfo sniper = Resources.Load<GunInfo>("Guns/Sniper");
        GunInfo shotgun = Resources.Load<GunInfo>("Guns/Shotgun");
        GunInfo peel = Resources.Load<GunInfo>("Guns/Peel");
        if (sniper != null && shotgun != null && rifle != null && peel != null)
        {
            Check(sb, sniper.maxRange > rifle.maxRange * 1.5f, "sniper owns long range",
                  $"{sniper.maxRange} vs rifle {rifle.maxRange}");
            Check(sb, shotgun.maxRange < rifle.maxRange * 0.3f, "shotgun is close quarters",
                  $"{shotgun.maxRange} vs rifle {rifle.maxRange}");
            Check(sb, shotgun.pelletsPerShot > 1, "shotgun fires pellets", $"{shotgun.pelletsPerShot}");
            Check(sb, peel.melee && peel.maxRange < 3f, "peel is melee", $"range {peel.maxRange}");
            Check(sb, sniper.damage >= 100f / 2f, "sniper is a two tap", $"{sniper.damage} damage");
        }

        // Loadout roll must never hand out the same gun twice
        bool dupes = false;
        for (int trial = 0; trial < 40; trial++)
        {
            string[] roll = WeaponLoadout.RandomSelection(3);
            if (roll.Length != 3 || roll[0] == roll[1] || roll[1] == roll[2] || roll[0] == roll[2])
                dupes = true;
        }
        Check(sb, !dupes, "random loadout has no duplicates", "40 rolls of 3");
        Check(sb, WeaponLoadout.GunGameLadder[WeaponLoadout.GunGameLadder.Length - 1] == "Peel",
              "gun game ends on melee", WeaponLoadout.GunGameLadder[WeaponLoadout.GunGameLadder.Length - 1]);

        // ---- hitboxes and headshots ----
        sb.AppendLine("[gun] ---------- hitboxes ----------");
        int hitLayer = LayerMask.NameToLayer(Hitbox.LayerName);
        int playerLayer = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        Check(sb, hitLayer >= 0, "Hitbox layer exists", hitLayer >= 0 ? $"layer {hitLayer}" : "missing");
        Check(sb, playerLayer >= 0, "Player layer exists", playerLayer >= 0 ? $"layer {playerLayer}" : "missing");

        GameObject dummy = new GameObject("~hitboxes");
        MonkeyRig dummyRig = dummy.AddComponent<MonkeyRig>();
        if (dummyRig.Build(false))
        {
            int built = Hitbox.BuildFor(dummy.transform, null);
            Check(sb, built >= 10, "hitboxes built on the rig", $"{built} volumes");

            Hitbox[] boxes = dummy.GetComponentsInChildren<Hitbox>(true);
            Hitbox head = System.Array.Find(boxes, b => b.partName == "head");
            Hitbox chest = System.Array.Find(boxes, b => b.partName == "chest");
            Hitbox leg = System.Array.Find(boxes, b => b.partName == "leg");

            Check(sb, head != null && head.multiplier > 1.5f, "headshots hurt more",
                  head == null ? "no head box" : $"x{head.multiplier}");
            Check(sb, chest != null && Mathf.Approximately(chest.multiplier, 1f), "chest is the baseline",
                  chest == null ? "-" : $"x{chest.multiplier}");
            Check(sb, leg != null && leg.multiplier < 1f, "legs hurt less",
                  leg == null ? "-" : $"x{leg.multiplier}");

            bool layered = true;
            foreach (Hitbox b in boxes)
                if (b.gameObject.layer != hitLayer) layered = false;
            Check(sb, layered, "all hitboxes on the Hitbox layer", $"{boxes.Length} checked");

            // A pistol headshot should be a two tap, not an instant kill
            GunInfo p2 = Resources.Load<GunInfo>("Guns/Pistol");
            if (p2 != null && head != null)
            {
                float hs = p2.damage * head.multiplier;
                Check(sb, hs < 100f && hs > 50f, "pistol headshot is a two tap",
                      $"{hs:F0} damage - {Mathf.CeilToInt(100f / hs)} to kill");
            }
        }
        Object.DestroyImmediate(dummy);

        // ---- damage falloff separates the roles ----
        sb.AppendLine("[gun] ---------- falloff ----------");
        GunInfo sg = Resources.Load<GunInfo>("Guns/Shotgun");
        GunInfo sn = Resources.Load<GunInfo>("Guns/Sniper");
        if (sg != null && sn != null && rifle != null)
        {
            float sgClose = sg.DamageAtRange(3f) * sg.pelletsPerShot;
            float sgFar = sg.DamageAtRange(20f) * sg.pelletsPerShot;
            Check(sb, sgClose > 100f, "shotgun kills point blank", $"{sgClose:F0} at 3m");
            Check(sb, sgFar < sgClose * 0.5f, "shotgun falls off hard",
                  $"{sgFar:F0} at 20m vs {sgClose:F0} at 3m");

            Check(sb, Mathf.Approximately(sn.DamageAtRange(300f), sn.damage), "sniper has no falloff",
                  $"{sn.DamageAtRange(300f):F0} at 300m");

            float rClose = rifle.DamageAtRange(10f), rFar = rifle.DamageAtRange(150f);
            Check(sb, rFar < rClose && rFar > rClose * 0.5f, "rifle falls off gently",
                  $"{rClose:F0} at 10m -> {rFar:F0} at 150m");
        }

        // ---- every weapon has its own voice ----
        foreach (string name in WeaponLoadout.GunGameLadder)
        {
            AudioClip[] clips = Resources.LoadAll<AudioClip>($"Audio/Shoot/{name}");
            Check(sb, clips.Length > 0, $"{name} has its own sound", $"{clips.Length} clip(s)");
        }

        Finish(sb);
    }

    static void Finish(StringBuilder sb)
    {
        sb.AppendLine($"[gun] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }
}
