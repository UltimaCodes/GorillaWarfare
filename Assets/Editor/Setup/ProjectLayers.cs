using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes sure the named layers this project's code looks up by name actually exist. Writes them
/// through SerializedObject on TagManager rather than editing TagManager.asset by hand - hand-edited
/// blank-slot whitespace in that file broke Unity's own parser once already (bug-log.md's
/// twenty-ninth pass).
/// </summary>
public static class ProjectLayers
{
    public static readonly string[] Required = { ShaderStack.ViewModelVolumeLayerName };

    [MenuItem("Tools/Gorilla Warfare/Add the project's named layers")]
    public static void Run()
    {
        SerializedObject tags = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tags.FindProperty("layers");
        int added = 0;

        foreach (string name in Required)
        {
            if (LayerMask.NameToLayer(name) >= 0)
                continue;

            int slot = -1;

            // 8 onwards - 0-7 are Unity's own.
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                Debug.LogError($"[layers] no free user layer for '{name}'");
                Finish(1);
                return;
            }

            layers.GetArrayElementAtIndex(slot).stringValue = name;
            added++;
            Debug.Log($"[layers] '{name}' -> layer {slot}");
        }

        tags.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();

        Debug.Log($"[layers] added {added}, {Required.Length - added} already present");
        Finish(0);
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
            EditorApplication.Exit(exitCode);
    }
}
