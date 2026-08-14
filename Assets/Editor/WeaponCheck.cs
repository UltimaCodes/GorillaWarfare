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

        GunInfo pistol = AssetDatabase.LoadAssetAtPath<GunInfo>("Assets/Items/Guns/Pistol.asset");
        GunInfo rifle = AssetDatabase.LoadAssetAtPath<GunInfo>("Assets/Items/Guns/Rifle.asset");

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

        Finish(sb);
    }

    static void Finish(StringBuilder sb)
    {
        sb.AppendLine($"[gun] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }
}
