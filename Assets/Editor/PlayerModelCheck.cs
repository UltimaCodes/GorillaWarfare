using System.Text;
using UnityEditor;
using UnityEngine;

// End-to-end check on the player model. Exists because "I verified the bit I just changed" kept
// passing while the actual result was broken - untextured, on its back, not animating.
//
// This builds the rig the same way the game does and reports every property that has to be true
// for the model to be acceptable. Run it before claiming the model works.
//
// Unity -batchmode -quit -executeMethod PlayerModelCheck.Run
public static class PlayerModelCheck
{
    const string fbx = "Assets/Resources/Models/Gorilla/gorilla.fbx";
    const string res = "Models/Gorilla/gorilla";

    static int failures;

    static void Check(StringBuilder sb, bool ok, string label, string detail)
    {
        if (!ok) failures++;
        sb.AppendLine($"[check] {(ok ? "PASS" : "FAIL")}  {label,-26} {detail}");
    }

    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("[check] ===== player model =====");
        failures = 0;

        // --- asset side
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (asset == null)
        {
            Debug.Log($"[check] FAIL  asset missing at {fbx}");
            return;
        }

        SkinnedMeshRenderer skin = asset.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Check(sb, skin != null, "skinned mesh", skin == null ? "none" : $"{skin.bones.Length} bones, {skin.sharedMesh.vertexCount} verts");

        // materials + textures - the thing that was never checked
        if (skin != null)
        {
            Material[] mats = skin.sharedMaterials;
            int withTex = 0;
            string names = "";
            foreach (Material m in mats)
            {
                if (m == null) continue;
                names += m.name + " ";
                if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) withTex++;
                else if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) withTex++;
            }
            Check(sb, mats.Length > 0, "material slots", $"{mats.Length} [{names.Trim()}]");
            Check(sb, withTex > 0, "diffuse texture bound", $"{withTex}/{mats.Length} materials have one");
        }

        // --- runtime side: build the rig exactly like the game does
        GameObject host = new GameObject("~check");
        MonkeyRig rig = host.AddComponent<MonkeyRig>();
        bool built = rig.Build(false);
        Check(sb, built, "rig built", built ? "bones resolved" : "Build() returned false");

        if (built)
        {
            Transform model = host.transform.childCount > 0 ? host.transform.GetChild(0) : null;
            if (model != null)
            {
                Vector3 e = model.localEulerAngles;
                Check(sb, Mathf.Abs(Mathf.DeltaAngle(e.x, -90f)) < 1f, "stood upright",
                      $"model localEuler = ({e.x:F0}, {e.y:F0}, {e.z:F0})");
            }

            Renderer r = host.GetComponentInChildren<Renderer>(true);
            if (r != null)
            {
                Bounds b = r.bounds;
                bool tall = b.size.y > 1.5f && b.size.y < 2.4f;
                Check(sb, tall, "world height ~1.9", $"bounds size = ({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})");
                bool notWider = b.size.x < b.size.y * 2f;
                Check(sb, notWider, "not absurdly wide", $"x/y ratio = {b.size.x / Mathf.Max(b.size.y, 0.01f):F2}");
            }

            // does driving it actually move a bone?
            Transform spine = FindBone(host.transform, "SPINE3");
            if (spine != null)
            {
                Quaternion before = spine.localRotation;
                rig.LookPitch = 45f;
                rig.SendMessage("LateUpdate", SendMessageOptions.DontRequireReceiver);
                float moved = Quaternion.Angle(before, spine.localRotation);
                Check(sb, moved > 1f, "aim moves the spine", $"rotated {moved:F1} degrees");
            }
            else
            {
                Check(sb, false, "aim moves the spine", "SPINE3 not found");
            }
        }

        Object.DestroyImmediate(host);

        sb.AppendLine($"[check] ===== {(failures == 0 ? "ALL PASS" : failures + " FAILURE(S)")} =====");
        Debug.Log(sb.ToString());
    }

    static Transform FindBone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
