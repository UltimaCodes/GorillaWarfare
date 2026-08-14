using System.Text;
using UnityEditor;
using UnityEngine;

// End-to-end check on the player model.
//
// Exists because "I verified the bit I just changed" kept passing while what actually spawned in
// game was untextured, on its back and 100x oversized. Two lessons are baked in:
//
//   1. Check the ASSET, not just the runtime result. If a model is only correct after a script
//      rotates it, the scene view lies to you forever and nobody can eyeball it.
//   2. Check it on the REAL player prefab, not a bare GameObject. A bare host has no parent
//      transform, so scale and offset problems that only appear once it's a child stay invisible.
//
// Unity -batchmode -quit -executeMethod PlayerModelCheck.Run
public static class PlayerModelCheck
{
    const string fbx = "Assets/Resources/Models/Gorilla/gorilla.fbx";
    const string playerPrefab = "Assets/Resources/PhotonPrefabs/PlayerController.prefab";

    static int failures;

    static void Check(StringBuilder sb, bool ok, string label, string detail)
    {
        if (!ok) failures++;
        sb.AppendLine($"[check] {(ok ? "PASS" : "FAIL")}  {label,-28} {detail}");
    }

    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        failures = 0;

        sb.AppendLine("[check] ---------- the asset on its own ----------");
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (asset == null)
        {
            Debug.Log($"[check] FAIL  asset missing at {fbx}");
            return;
        }

        SkinnedMeshRenderer skin = asset.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Check(sb, skin != null, "skinned mesh",
              skin == null ? "none" : $"{skin.bones.Length} bones, {skin.sharedMesh.vertexCount} verts");

        if (skin != null)
        {
            Material am = skin.sharedMaterial;
            bool named = am != null && am.name != "Default-Material";
            bool tex = am != null && ((am.HasProperty("_MainTex") && am.GetTexture("_MainTex") != null)
                                   || (am.HasProperty("_BaseMap") && am.GetTexture("_BaseMap") != null));
            bool nrm = am != null && am.HasProperty("_BumpMap") && am.GetTexture("_BumpMap") != null;
            Check(sb, named, "own material", am == null ? "none" : am.name);
            Check(sb, tex, "diffuse on asset", $"{tex}");
            Check(sb, nrm, "normal map on asset", $"{nrm}");

            Bounds mb = skin.sharedMesh.bounds;
            Check(sb, mb.size.y > 1.6f && mb.size.y < 2.2f, "asset height on Y",
                  $"bounds = ({mb.size.x:F2}, {mb.size.y:F2}, {mb.size.z:F2})");
            Check(sb, mb.size.y > mb.size.z, "upright not on its back",
                  $"y {mb.size.y:F2} must exceed z {mb.size.z:F2}");
        }

        Check(sb, asset.GetComponentInChildren<Animator>(true) == null, "no animator on asset",
              "an animator stamps its own pose over the bones");

        sb.AppendLine("[check] ---------- as the game spawns it ----------");
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(playerPrefab);
        if (prefabAsset == null)
        {
            Check(sb, false, "player prefab", "missing");
            Finish(sb);
            return;
        }

        // The rig decides grounded with a downward raycast, so without a floor it holds the
        // airborne pose and the legs correctly never swing. Give it something to stand on.
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0f, -1.5f, 0f);
        floor.transform.localScale = new Vector3(200f, 1f, 200f);

        // Edit mode doesn't run the physics loop, so a freshly created collider isn't in the
        // scene's broadphase until transforms are pushed across.
        Physics.SyncTransforms();

        GameObject player = Object.Instantiate(prefabAsset);
        MonkeyRig rig = player.AddComponent<MonkeyRig>();
        bool built = rig.Build(false);
        Check(sb, built, "rig builds on player", built ? "bones resolved" : "Build() returned false");

        if (built)
        {
            Transform model = null;
            foreach (Transform t in player.transform)
                if (t.name.ToLower().Contains("gorilla")) { model = t; break; }

            Check(sb, model != null, "model parented to player", model == null ? "not found" : model.name);

            if (model != null)
            {
                Vector3 e = model.localEulerAngles;
                Check(sb, Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 1f && Mathf.Abs(Mathf.DeltaAngle(e.z, 0f)) < 1f,
                      "no rotation compensation", $"localEuler = ({e.x:F0}, {e.y:F0}, {e.z:F0})");
                Check(sb, Mathf.Abs(model.lossyScale.x - 1f) < 0.05f, "no inherited scale",
                      $"lossyScale = {model.lossyScale.x:F3}");
            }

            SkinnedMeshRenderer live = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (live != null)
            {
                Check(sb, live.enabled && live.gameObject.activeInHierarchy, "renderer live",
                      $"enabled={live.enabled} active={live.gameObject.activeInHierarchy}");

                Bounds lb = live.sharedMesh.bounds;
                Matrix4x4 m = (model != null ? model : live.transform).localToWorldMatrix;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                }
                Vector3 size = max - min;
                Check(sb, size.y > 1.6f && size.y < 2.2f, "world height ~1.9",
                      $"size = ({size.x:F2}, {size.y:F2}, {size.z:F2})");

                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null)
                {
                    float capsuleBottom = player.transform.position.y + cc.center.y - cc.height * 0.5f;
                    Check(sb, Mathf.Abs(min.y - capsuleBottom) < 0.25f, "feet on capsule floor",
                          $"feet {min.y:F2} vs capsule bottom {capsuleBottom:F2}");
                }
            }
            else
            {
                Check(sb, false, "renderer live", "no SkinnedMeshRenderer under the player");
            }

            Transform spine = Find(player.transform, "SPINE3");
            Transform thigh = Find(player.transform, "LEFTHIP");
            Check(sb, thigh != null, "leg bone present", thigh == null ? "LEFTHIP missing" : thigh.name);

            if (spine != null)
            {
                Quaternion s0 = spine.localRotation;
                rig.LookPitch = 45f;
                rig.Tick(1f / 60f);
                Check(sb, Quaternion.Angle(s0, spine.localRotation) > 1f, "aim drives the spine",
                      $"{Quaternion.Angle(s0, spine.localRotation):F1} degrees");
            }
            else
            {
                Check(sb, false, "aim drives the spine", "SPINE3 missing");
            }

            // The legs are the thing that was never tested. MonkeyRig works speed out from
            // position deltas, so standing still it correctly does nothing - we have to actually
            // move the player between ticks to see the gait respond.
            if (thigh != null)
            {
                // Prime it with one tick so planarSpeed has a value, then walk.
                Check(sb, rig.GroundedForTest, "grounded on the floor", $"{rig.GroundedForTest}");

                // Sample the whole walk and take the widest spread. Comparing start to end is
                // useless - the gait is a sine, so if the distance happens to land on a whole
                // cycle it reads zero while the legs are swinging perfectly well. That is
                // exactly what happened at 2.4m: phase came out at 6.24, i.e. 2 pi.
                float minAng = 999f, maxAng = -999f;
                for (int i = 0; i < 40; i++)
                {
                    player.transform.position += Vector3.forward * 0.06f;   // ~3.6 m/s at 60fps
                    Physics.SyncTransforms();
                    rig.Tick(1f / 60f);

                    float a = Quaternion.Angle(Quaternion.identity, thigh.localRotation);
                    minAng = Mathf.Min(minAng, a);
                    maxAng = Mathf.Max(maxAng, a);
                }
                float spread = maxAng - minAng;
                sb.AppendLine($"[check]       rig state: {rig.DebugState}");
                Check(sb, spread > 15f, "walking swings the legs",
                      $"swing spread {spread:F1} degrees over 40 ticks (want > 15)");
            }

            // owner's own body must be invisible to them but still cast a shadow
            GameObject hidden = Object.Instantiate(prefabAsset);
            MonkeyRig hiddenRig = hidden.AddComponent<MonkeyRig>();
            if (hiddenRig.Build(true))
            {
                Renderer hr = hidden.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Check(sb, hr != null && hr.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly,
                      "own body hidden, casts shadow",
                      hr == null ? "no renderer" : hr.shadowCastingMode.ToString());
            }
            Object.DestroyImmediate(hidden);
        }

        Object.DestroyImmediate(player);
        Object.DestroyImmediate(floor);
        Finish(sb);
    }

    static void Finish(StringBuilder sb)
    {
        sb.AppendLine($"[check] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
