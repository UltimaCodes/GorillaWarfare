using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Batch-mode helper for inspecting an imported model without opening the editor.
// Run: Unity -batchmode -quit -executeMethod ModelProbe.Probe
public static class ModelProbe
{
    const string path = "Assets/Resources/Models/Monkey/monkey.fbx";

    public static void Probe()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"--- probing {path}");

        Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
        sb.AppendLine($"sub-assets: {all.Length}");
        foreach (var group in all.GroupBy(o => o.GetType().Name))
            sb.AppendLine($"  {group.Key} x{group.Count()}");

        AnimationClip[] clips = all.OfType<AnimationClip>().ToArray();
        sb.AppendLine($"clips: {clips.Length}");
        foreach (AnimationClip c in clips)
            sb.AppendLine($"  '{c.name}'  {c.length:F2}s  {c.frameRate}fps  legacy={c.legacy}");

        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null)
        {
            sb.AppendLine("!! model failed to load");
            Debug.Log(sb.ToString());
            return;
        }

        SkinnedMeshRenderer[] skins = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        sb.AppendLine($"SkinnedMeshRenderers: {skins.Length}");
        foreach (SkinnedMeshRenderer s in skins)
            sb.AppendLine($"  {s.name}: bones={s.bones.Length} verts={s.sharedMesh.vertexCount} subMeshes={s.sharedMesh.subMeshCount}");

        Transform[] bones = go.GetComponentsInChildren<Transform>(true);
        sb.AppendLine($"transforms: {bones.Length}");
        sb.AppendLine("first 25 bone names:");
        foreach (Transform t in bones.Take(25))
            sb.AppendLine($"  {t.name}");

        Bounds b = skins.Length > 0 ? skins[0].sharedMesh.bounds : new Bounds();
        sb.AppendLine($"mesh bounds size: {b.size} (tells us the import scale)");

        Debug.Log(sb.ToString());
    }

    // Does this rig map to Unity's Humanoid? If it does, Mixamo animations can be retargeted
    // onto it, which is the difference between having a full locomotion set and having whatever
    // shipped in the fbx.
    public static void TryHumanoid()
    {
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.Log("--- no ModelImporter");
            return;
        }

        ModelImporterAnimationType before = importer.animationType;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.SaveAndReimport();

        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Animator animator = go != null ? go.GetComponent<Animator>() : null;
        Avatar avatar = animator != null ? animator.avatar : null;

        Debug.Log($"--- humanoid attempt: avatar={(avatar == null ? "NULL" : avatar.name)} " +
                  $"isValid={(avatar != null && avatar.isValid)} isHuman={(avatar != null && avatar.isHuman)}");

        if (avatar == null || !avatar.isValid)
        {
            importer.animationType = before;
            importer.SaveAndReimport();
            Debug.Log("--- reverted to " + before);
        }
    }

    // Why is it T-posing? Instantiate what the game actually loads and look at it.
    public static void ProbeRuntimeModel()
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Monkey/monkey");
        if (prefab == null) { Debug.Log("[rt] prefab NOT FOUND at Resources/Models/Monkey/monkey"); return; }

        GameObject inst = Object.Instantiate(prefab);
        Animator anim = inst.GetComponentInChildren<Animator>();

        string[] wanted = { "b_Spine02", "b_Head", "b_Left_Leg01", "b_Left_Leg02",
                            "b_Right_Leg01", "b_Left_UpperArm", "b_Right_Hand" };
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("[rt] --- runtime model probe");
        sb.AppendLine($"[rt] Animator present: {anim != null}");
        if (anim != null)
        {
            sb.AppendLine($"[rt]   avatar={(anim.avatar == null ? "none" : anim.avatar.name)} " +
                          $"isHuman={(anim.avatar != null && anim.avatar.isHuman)} " +
                          $"controller={(anim.runtimeAnimatorController == null ? "NONE" : anim.runtimeAnimatorController.name)}");
            sb.AppendLine($"[rt]   cullingMode={anim.cullingMode} applyRootMotion={anim.applyRootMotion}");
        }
        foreach (string w in wanted)
        {
            bool found = false;
            foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                if (t.name == w) { found = true; break; }
            sb.AppendLine($"[rt]   bone {w,-18} {(found ? "found" : "MISSING")}");
        }
        Debug.Log(sb.ToString());
        Object.DestroyImmediate(inst);
    }
}
