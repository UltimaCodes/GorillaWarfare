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

        // The asset has to be correct on its own - drag it into a scene and it should look
        // right. Anything fixed only at runtime means the editor lies to you.
        if (skin != null)
        {
            Material am = skin.sharedMaterial;
            bool assetTex = am != null && ((am.HasProperty("_MainTex") && am.GetTexture("_MainTex") != null)
                                        || (am.HasProperty("_BaseMap") && am.GetTexture("_BaseMap") != null));
            Check(sb, assetTex, "ASSET textured", am == null ? "no material" : $"{am.name}, textured={assetTex}");

            Bounds mb = skin.sharedMesh.bounds;
            Check(sb, mb.size.y > 1.5f && mb.size.y < 2.4f, "ASSET upright + scaled",
                  $"mesh bounds = ({mb.size.x:F2}, {mb.size.y:F2}, {mb.size.z:F2}) - y must be the height");
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
                // No runtime rotation any more - the fbx is baked upright, so this should be
                // identity. If it isn't, something is compensating again.
                Vector3 e = model.localEulerAngles;
                Check(sb, Mathf.Abs(Mathf.DeltaAngle(e.x, 0f)) < 1f, "no runtime rotation hack",
                      $"model localEuler = ({e.x:F0}, {e.y:F0}, {e.z:F0})");
            }

            // Material as the player actually sees it - bound at runtime, not on the asset.
            Renderer r = host.GetComponentInChildren<Renderer>(true);
            if (r != null)
            {
                Material m = r.sharedMaterial;
                bool hasDiffuse = m != null && m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null;
                bool hasNormal = m != null && m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") != null;
                Check(sb, hasDiffuse, "diffuse bound", m == null ? "no material" : $"{m.name}, diffuse={hasDiffuse}");
                Check(sb, hasNormal, "normal map bound", $"normal={hasNormal}");

                // A SkinnedMeshRenderer's geometry follows its bones, not its own transform, so
                // r.transform tells us nothing - it measured local space, where height is still
                // on Z. Renderer.bounds would be right but is stale in edit mode. Push the mesh
                // corners through the model root, which is what carries the upright rotation.
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                Bounds local = smr != null ? smr.sharedMesh.bounds : r.localBounds;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = local.center + Vector3.Scale(local.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 w = (model != null ? model : r.transform).localToWorldMatrix.MultiplyPoint3x4(c);
                    min = Vector3.Min(min, w); max = Vector3.Max(max, w);
                }
                Vector3 size = max - min;
                bool tall = size.y > 1.5f && size.y < 2.4f;
                Check(sb, tall, "world height ~1.9", $"size = ({size.x:F2}, {size.y:F2}, {size.z:F2})");
                Check(sb, size.x < size.y * 2f, "sane proportions", $"span/height = {size.x / Mathf.Max(size.y, 0.01f):F2}");
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
