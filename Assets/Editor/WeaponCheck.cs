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
        //
        // All five, with their own bounds. It used to check the pistol and the rifle and take
        // the rest on trust, which is how a sniper could be any length at all - and it is the
        // one most likely to be wrong, being seven times the length of the fruit it came from.
        (string weapon, float min, float max)[] models =
        {
            ("Pistol",  0.20f, 0.45f),
            ("Shotgun", 0.45f, 0.85f),
            ("Rifle",   0.45f, 0.85f),
            ("Sniper",  1.00f, 1.90f),   // absurd on purpose
            ("Peel",    0.12f, 0.35f),
        };

        foreach ((string weapon, float min, float max) in models)
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
                Check(sb, longest > min && longest < max, $"banana{weapon} is weapon sized",
                      $"longest axis {longest:F2}m");
                Check(sb, b.size.z >= longest - 0.001f, $"banana{weapon} points forward",
                      $"z {b.size.z:F2} should be the longest of ({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})");
            }
        }

        // Each weapon needs its own colour and its own silhouette - if they all look alike you
        // genuinely cannot tell which gun you're holding.
        var seenColours = new System.Collections.Generic.List<Color>();
        var seenVerts = new System.Collections.Generic.List<int>();
        foreach (string name in WeaponLoadout.GunGameLadder)
        {
            Material wm = Resources.Load<Material>($"Models/Weapons/Banana{name}Mat");
            Check(sb, wm != null, $"{name} has its own material", wm == null ? "missing" : wm.name);

            if (wm != null)
            {
                bool unique = true;
                foreach (Color c in seenColours)
                    if (Vector4.Distance(c, wm.color) < 0.12f) unique = false;
                Check(sb, unique, $"{name} colour is distinct", ColorToStr(wm.color));
                seenColours.Add(wm.color);
            }

            GameObject bmesh = Resources.Load<GameObject>($"Models/Weapons/Banana{name}");
            MeshFilter bmf = bmesh != null ? bmesh.GetComponentInChildren<MeshFilter>(true) : null;
            if (bmf != null)
                seenVerts.Add(bmf.sharedMesh.vertexCount);
        }

        Check(sb, seenColours.Count == 5, "five distinct colours", $"{seenColours.Count}");

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

        // ---- can the owner actually SEE their weapon ----
        // The pivot is the grip, and the grip is SUPPOSED to sit at or below the bottom edge -
        // that's what a hand entering frame from below looks like. What matters is how much of
        // the banana body is on screen, so measure the mesh, not the transform.
        sb.AppendLine("[gun] ---------- visibility ----------");
        GameObject pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/PhotonPrefabs/PlayerController.prefab");
        if (pf != null)
        {
            GameObject inst = Object.Instantiate(pf);
            Camera camera = inst.GetComponentInChildren<Camera>(true);

            Transform viewHolder = null;
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                if (t.name == "ItemHolder") { viewHolder = t; break; }

            Check(sb, camera != null && viewHolder != null, "camera and holder found",
                  $"camera={camera != null} holder={viewHolder != null}");

            if (camera != null && viewHolder != null)
            {
                SerializedObject so2 = new SerializedObject(inst.GetComponent<PlayerController>());
                viewHolder.localPosition = so2.FindProperty("weaponViewOffset").vector3Value;
                Vector3 rot = so2.FindProperty("weaponViewRotation").vector3Value;
                viewHolder.localRotation = Quaternion.Euler(rot);

                Check(sb, Mathf.Abs(rot.y) > 5f, "angled across the view", $"yaw {rot.y:F0}");

                // Used to assert a steep pitch, which stopped meaning anything once the weapon
                // models were anchored by the grip - the same angle now produces a completely
                // different result on screen. Assert what was actually wanted instead: the far
                // end of the banana sits higher in frame than the end you're holding.
                Vector3 gripPoint = viewHolder.position;
                Vector3 tipPoint = viewHolder.TransformPoint(Vector3.forward * 0.5f);

                Vector3 gripView = camera.WorldToViewportPoint(gripPoint);
                Vector3 tipView = camera.WorldToViewportPoint(tipPoint);

                Check(sb, tipView.y > gripView.y, "rises across the frame, not aimed away",
                      $"grip at y {gripView.y:F2}, tip at y {tipView.y:F2}");

                // Anything nearer than the 0.3 near clip gets sliced open. The sniper is 1.5m
                // and used to have half its length behind the camera.
                Check(sb, gripView.z > 0.3f, "the grip clears the near clip plane",
                      $"{gripView.z:F2}m in front of the camera");

                // Low and to one side. A weapon through the middle of the screen is a weapon
                // covering the thing you are shooting at.
                Check(sb, gripView.y < 0.45f && gripView.x > 0.5f, "held low and to the right",
                      $"grip viewport {gripView.x:F2},{gripView.y:F2}");

                float meshOnScreen = FractionOnScreen(camera, viewHolder, "Models/Weapons/BananaRifle", Vector3.zero, Vector3.zero);
                Check(sb, meshOnScreen > 0.45f, "most of the banana is on screen",
                      $"{meshOnScreen * 100f:F0}% of its bounds");

            }
            Object.DestroyImmediate(inst);
        }

        // ---- the bugs Ryaan found ----
        sb.AppendLine("[gun] ---------- regressions ----------");

        // Reload must not depend on a coroutine, because switching weapons deactivates the gun
        // and kills its coroutines - that bricked the weapon for the rest of the life.
        string gunSrc = System.IO.File.ReadAllText("Assets/Scripts/SingleShotGun.cs");
        Check(sb, !gunSrc.Contains("StartCoroutine(ReloadRoutine"), "reload survives a weapon switch",
              "no coroutine in the reload path");
        Check(sb, gunSrc.Contains("reloadDoneAt"), "reload is timestamp driven", "reloadDoneAt present");

        // Decals used to be forbidden from parenting to players, because a shot that stopped on
        // our own hitbox left one hanging in front of the camera. The trace excludes the shooter
        // now, so the opposite rule applies: a mark parents to whatever it landed on, so blood
        // moves with a body and goes when that body does.
        string decalSrc = System.IO.File.ReadAllText("Assets/Scripts/BulletDecal.cs");

        Check(sb, gunSrc.Contains("BulletDecal.Spawn"), "shots leave a mark through BulletDecal",
              "no prefab quad");

        Check(sb, decalSrc.Contains("SetParent(hit.collider.transform"),
              "a mark sticks to what it hit", "parented to the hit collider");

        Check(sb, decalSrc.Contains("if (anchor == null)"),
              "a mark dies with what it was on", "destroys itself when the anchor is gone");

        Check(sb, decalSrc.Contains("Physics.Raycast"),
              "a mark confirms there is a surface", "re-traced locally before drawing");

        // The trace has to skip our own hitboxes rather than stopping on them.
        Check(sb, gunSrc.Contains("RaycastNonAlloc"), "trace skips our own hitboxes",
              "RaycastNonAlloc, nearest non-self hit");

        // Every weapon is the same banana at a different size, so they all share one texture.
        // Without it they render flat coloured, which is what they looked like before.
        Texture albedo = Resources.Load<Texture>("Models/Weapons/BananaAlbedo");
        Check(sb, albedo != null, "the banana texture is in Resources",
              albedo == null ? "missing" : $"{albedo.width}x{albedo.height}");

        foreach ((string weapon, float _, float __) in models)
        {
            Material mat = Resources.Load<Material>($"Models/Weapons/Banana{weapon}Mat");
            Check(sb, mat != null && mat.mainTexture != null, $"banana{weapon} is textured",
                  mat == null ? "no material" : mat.mainTexture != null ? mat.mainTexture.name : "flat colour");
        }


        // ---- recoil is no longer forgiving ----
        GunInfo rf = Resources.Load<GunInfo>("Guns/Rifle");
        if (rf != null)
        {
            float climb = 0f;
            for (int i = 0; i < 30; i++) climb += rf.RecoilForShot(i).x;
            Check(sb, climb > 20f, "a full spray climbs a long way", $"{climb:F0} degrees over 30 rounds");

            // and it has to hold that climb while you fire, not decay back on its own
            string pcSrc = System.IO.File.ReadAllText("Assets/Scripts/PlayerController.cs");
            Check(sb, pcSrc.Contains("Time.time - lastRecoilAt > recoilHoldTime"),
                  "recoil holds while firing", "recovery waits for you to release");
            Check(sb, rf.recoilRecovery < 0.6f, "recoil is not handed back for free",
                  $"recovery {rf.recoilRecovery}");
        }

        // ---- crosshair must not double count recoil ----
        string hudSrc = System.IO.File.ReadAllText("Assets/Scripts/CombatHud.cs");
        Check(sb, !hudSrc.Contains("cy -= recoil.x"), "crosshair stays at screen centre",
              "recoil already rotates the camera, so shots leave from centre");

        // ---- ripeness ----
        sb.AppendLine("[gun] ---------- ripeness ----------");
        GunInfo rifleInfo = Resources.Load<GunInfo>("Guns/Rifle");
        if (rifleInfo != null)
        {
            Color full = rifleInfo.RipenessFor(rifleInfo.magazineSize);
            Color half = rifleInfo.RipenessFor(rifleInfo.magazineSize / 2);
            Color empty = rifleInfo.RipenessFor(0);

            Check(sb, full.g > full.r, "full magazine is green", ColorToStr(full));
            Check(sb, half.r > half.b && half.g > half.b, "half spent is yellow", ColorToStr(half));
            Check(sb, empty.r < 0.5f && empty.g < 0.4f, "empty is brown", ColorToStr(empty));

            // it has to actually move, not just be three constants
            Check(sb, Vector4.Distance(full, half) > 0.15f && Vector4.Distance(half, empty) > 0.2f,
                  "ripeness changes visibly", "each stage differs");
        }

        // ---- spare magazines ----
        foreach (string name in new[] { "Pistol", "Rifle", "Shotgun", "Sniper" })
        {
            GunInfo g2 = Resources.Load<GunInfo>("Guns/" + name);
            if (g2 == null) continue;
            Check(sb, g2.spareMagazines > 0, $"{name} carries spares",
                  $"{g2.spareMagazines} bananas = {g2.spareMagazines * g2.magazineSize} rounds in reserve");
        }

        // ---- a scope has to be worth using ----
        //
        // Big Mike's spread was zero, which made it pinpoint from the hip and left the scope as
        // a zoom with no reason to pull it up under pressure. Aiming has to buy accuracy you
        // don't otherwise have, or it's decoration.
        foreach (string weapon in new[] { "Pistol", "Shotgun", "Rifle", "Sniper", "Peel" })
        {
            GunInfo g = Resources.Load<GunInfo>($"Guns/{weapon}");
            if (g == null || !g.canAim)
                continue;

            Check(sb, g.spread > 0.5f, $"{weapon} is worth scoping",
                  $"hip spread {g.spread:F1} degrees against {g.spread * g.aimSpreadScale:F2} scoped");

            Check(sb, g.aimFov < 45f, $"{weapon} actually magnifies",
                  $"{g.aimFov:F0} degree field of view");
        }

        // ---- shapes match the brief ----
        sb.AppendLine("[gun] ---------- shapes ----------");
        var lengths = new System.Collections.Generic.Dictionary<string, float>();
        foreach (string name in WeaponLoadout.GunGameLadder)
        {
            GameObject m = Resources.Load<GameObject>($"Models/Weapons/Banana{name}");
            MeshFilter mf3 = m != null ? m.GetComponentInChildren<MeshFilter>(true) : null;
            if (mf3 == null) continue;
            Bounds b3 = mf3.sharedMesh.bounds;
            lengths[name] = b3.size.z;
            sb.AppendLine($"[gun]       {name,-9} {b3.size.z:F2}m long, {mf3.sharedMesh.vertexCount} verts");
        }
        if (lengths.Count == 5)
        {
            // Was 1.8x. Big Mike came down from 1.45m to 1.18m because at full length it cut
            // across the crosshair, which is a real cost for a joke about a long banana. It has
            // to stay clearly the longest thing in the game; it doesn't have to be unusable.
            Check(sb, lengths["Sniper"] > lengths["Rifle"] * 1.5f, "sniper is obnoxiously long",
                  $"{lengths["Sniper"]:F2}m vs rifle {lengths["Rifle"]:F2}m");
            Check(sb, lengths["Rifle"] > lengths["Pistol"] * 1.8f, "rifle is a longer banana",
                  $"{lengths["Rifle"]:F2}m vs pistol {lengths["Pistol"]:F2}m");

            GameObject sgm = Resources.Load<GameObject>("Models/Weapons/BananaShotgun");
            MeshFilter sgf = sgm.GetComponentInChildren<MeshFilter>(true);
            Check(sb, sgf.sharedMesh.bounds.size.x > 0.15f, "shotgun is two bananas wide",
                  $"{sgf.sharedMesh.bounds.size.x:F2}m across");
        }

        Finish(sb);
    }

    /// What fraction of a model's bounding box corners land inside the viewport when placed
    /// under the weapon holder. Corner sampling rather than the pivot, because a pivot can be
    /// off screen while the object is perfectly visible - which is exactly what caught us out.
    static float FractionOnScreen(Camera cam, Transform holder, string resource, Vector3 offset, Vector3 euler)
    {
        GameObject prefab = Resources.Load<GameObject>(resource);
        if (prefab == null)
            return 0f;

        GameObject inst = Object.Instantiate(prefab, holder);
        inst.transform.localPosition = offset;
        inst.transform.localRotation = Quaternion.Euler(euler);

        MeshFilter mf = inst.GetComponentInChildren<MeshFilter>(true);
        if (mf == null)
        {
            Object.DestroyImmediate(inst);
            return 0f;
        }

        Bounds b = mf.sharedMesh.bounds;
        int inside = 0;
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = b.center + Vector3.Scale(b.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 vp = cam.WorldToViewportPoint(mf.transform.TransformPoint(c));
            if (vp.z > cam.nearClipPlane && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f)
                inside++;
        }

        Object.DestroyImmediate(inst);
        return inside / 8f;
    }

    static string ColorToStr(Color c) => $"({c.r:F2}, {c.g:F2}, {c.b:F2})";

    static void Finish(StringBuilder sb)
    {
        sb.AppendLine($"[gun] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }
}
