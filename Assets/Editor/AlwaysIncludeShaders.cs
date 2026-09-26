using UnityEditor;
using UnityEngine;

/// <summary>
/// Registers this project's own runtime-looked-up shaders ("Custom/ScreenOutline",
/// "Hidden/Gorilla Warfare/PSX Filter") in Graphics Settings' Always Included Shaders list.
///
/// Both are found at runtime with <c>Shader.Find</c> rather than referenced by any material, and
/// neither is dragged onto anything in a scene - which is exactly the condition under which
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
