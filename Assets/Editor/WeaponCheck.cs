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

        Finish(sb);
    }

    static void Finish(StringBuilder sb)
    {
        sb.AppendLine($"[gun] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }
}
