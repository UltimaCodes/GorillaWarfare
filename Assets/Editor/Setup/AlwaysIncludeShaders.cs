using UnityEditor;
using UnityEngine;

/// <summary>
/// Registers this project's own runtime-looked-up shaders ("Custom/ScreenOutline",
/// "Hidden/Gorilla Warfare/PSX Filter", and the vine's "Custom/UnlitVertexColor") in Graphics
/// Settings' Always Included Shaders list.
///
/// All three are found at runtime with <c>Shader.Find</c> rather than referenced by any material,
/// and none is dragged onto anything in a scene - which is exactly the condition under which
/// Unity's shader stripping can and will cut an unreferenced shader from a build. In the Editor
/// both keep working regardless, because the Editor never strips anything, which is what let the
/// outline going missing "in the actual game" (a built player, not Play Mode) go unnoticed for as
/// long as it did. Always Included Shaders is the one list stripping never touches.
///
/// A GUID typed by hand here would be a guess - <see cref="SerializedObject"/> on the settings
/// asset lets Unity assign the real one from the actual <see cref="Shader"/> reference instead.
/// </summary>
public static class AlwaysIncludeShaders
{
    static readonly string[] ShaderNames =
    {
        "Custom/ScreenOutline",
        "Hidden/Gorilla Warfare/PSX Filter",

        // VineGrapple's rope. Without it a build falls back to Sprites/Default, which is
        // transparent, so the rope silently drops out of the toon outline.
        "Custom/UnlitVertexColor",

        // Built in, but still stripped when no material in the build uses them - and nothing
        // does: every flash, explosion, impact, flame, burn and scorch in the game makes its
        // material at runtime from these by name. Missing, each falls back to Sprites/Default,
        // which blends normally - no additive glow on fire, no darkening on a scorch. Added
        // 2026-09-28 with the chili's burn and char.
        "Legacy Shaders/Particles/Additive",
        "Legacy Shaders/Particles/Alpha Blended",
        "Legacy Shaders/Particles/Multiply",

        // Marks on a body - char and blood (BodyMarks adds it as an extra pass while any show),
        // and on the world - every impact, stray blood and char decal (BulletDecal).
        "Custom/BodyMarks",
        "Custom/SurfaceMark",

        // Weapon skins - WeaponSkins makes a material from it for every finish it draws. Added
        // 2026-09-30 with the skins.
        "Custom/WeaponFinish",
    };

    [MenuItem("Tools/Gorilla Warfare/Always-include the custom shaders")]
    public static void Run()
    {
        Object settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        SerializedObject serialized = new SerializedObject(settings);
        SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");

        int added = 0;

        foreach (string name in ShaderNames)
        {
            Shader shader = Shader.Find(name);

            if (shader == null)
            {
                Debug.LogError($"[shaders] could not find '{name}' to always-include it");
                continue;
            }

            bool present = false;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    present = true;
                    break;
                }
            }

            if (present)
                continue;

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            added++;
        }

        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log($"[shaders] always-included {added} shader(s), {ShaderNames.Length - added} already present");
        Finish(0);
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
