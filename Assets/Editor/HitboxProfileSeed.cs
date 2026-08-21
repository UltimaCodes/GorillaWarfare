using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time creation of Assets/Resources/HitboxProfile.asset with sane starting numbers, not the
/// broken fitted values - a rough taper from the torso out to the extremities, small enough to
/// clearly separate head/body/limbs, meant to be opened and dragged from here rather than trusted
/// as-is. See HitboxProfile.cs for why this is hand-set instead of measured.
/// </summary>
public static class HitboxProfileSeed
{
    [MenuItem("Tools/Gorilla Warfare/Create the hitbox profile (if missing)")]
    public static void Run()
    {
        const string path = "Assets/Resources/HitboxProfile.asset";

        if (AssetDatabase.LoadAssetAtPath<HitboxProfile>(path) != null)
        {
            Debug.Log("[hitbox] profile already exists, left alone");
            if (Application.isBatchMode) EditorApplication.Exit(0);
            return;
        }

        HitboxProfile profile = ScriptableObject.CreateInstance<HitboxProfile>();

        profile.entries = new[]
        {
            Entry("Head",          "head",    0.24f),
            Entry("NECK",          "neck",    0.20f),
            Entry("SPINE3",        "chest",   0.32f),
            Entry("SPINE1",        "stomach", 0.30f),
            Entry("HIPS",          "hips",    0.28f),
            Entry("LEFTHIP",       "leg",     0.19f),
            Entry("RIGHTHIP",      "leg",     0.19f),
            Entry("LEFTKNEE",      "leg",     0.17f),
            Entry("RIGHTKNEE",     "leg",     0.17f),
            Entry("LEFTSHOULDER",  "arm",     0.18f),
            Entry("RIGHTSHOULDER", "arm",     0.18f),
            Entry("LEFTELBOW",     "arm",     0.16f),
            Entry("RIGHTELBOW",    "arm",     0.16f),
        };

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        AssetDatabase.CreateAsset(profile, path);
        AssetDatabase.SaveAssets();

        Debug.Log($"[hitbox] created {path} with {profile.entries.Length} starting entries");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static HitboxProfile.Entry Entry(string bone, string label, float radius) => new HitboxProfile.Entry
    {
        bone = bone,
        label = label,
        radius = radius,
    };
}
