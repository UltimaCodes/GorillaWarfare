using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Writes out everything the menu scene is wired to: every object, what's on it, what every button
/// and input field calls, and every scene reference the menu scripts hold. For checking a rebuild
/// kept every connection the old one had - a button whose OnClick lost its target doesn't error,
/// it just does nothing when clicked. Output: Logs/menu-wiring.txt.
/// </summary>
public static class MenuWiringReport
{
    const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Gorilla Warfare/Report the menu wiring")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StringBuilder sb = new StringBuilder();

        foreach (GameObject root in scene.GetRootGameObjects())
            Walk(root.transform, 0, sb);

        sb.AppendLine();
        sb.AppendLine("==== serialized references on menu scripts ====");

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb is Graphic || mb is Selectable || mb is LayoutGroup || mb is ContentSizeFitter
                    || mb is CanvasScaler || mb is GraphicRaycaster || mb is LayoutElement || mb is ScrollRect
                    || mb is Mask || mb is RectMask2D || mb.GetType().Namespace == "UnityEngine.EventSystems")
                    continue;

                SerializedObject so = new SerializedObject(mb);
                SerializedProperty it = so.GetIterator();
                List<string> refs = new List<string>();

                while (it.NextVisible(true))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.name != "m_Script")
                        refs.Add($"{it.propertyPath} = {Describe(it.objectReferenceValue)}");
                    else if (it.propertyType == SerializedPropertyType.String && it.depth == 0 && it.name != "m_Name")
                        refs.Add($"{it.propertyPath} = \"{it.stringValue}\"");
                }

                if (refs.Count > 0)
                    sb.AppendLine($"{PathOf(mb.transform)} [{mb.GetType().Name}]\n    " + string.Join("\n    ", refs));
            }
        }

        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/menu-wiring.txt", sb.ToString());
        Debug.Log($"[menu] wiring report written, {sb.Length} chars");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    /// The Game scene's root objects and what's on each - for deciding what the menu backdrop
    /// copies (geometry, lights, grass) and what it must leave behind (spawning, match state, HUD).
    [MenuItem("Tools/Gorilla Warfare/Report the game scene roots")]
    public static void GameRoots()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
        StringBuilder sb = new StringBuilder();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Dictionary<string, int> kinds = new Dictionary<string, int>();

            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                string kind = c == null ? "MISSING" : c.GetType().Name;
                if (kind == "Transform") continue;
                kinds[kind] = kinds.TryGetValue(kind, out int n) ? n + 1 : 1;
            }

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> k in kinds)
                parts.Add($"{k.Key} x{k.Value}");

            sb.AppendLine($"{(root.activeSelf ? "" : "(off) ")}{root.name}  children={root.transform.childCount}  {string.Join(", ", parts)}");
        }

        sb.AppendLine();
        sb.AppendLine($"skybox={(RenderSettings.skybox != null ? RenderSettings.skybox.name : "none")} ambientMode={RenderSettings.ambientMode} "
                      + $"fog={RenderSettings.fog} fogColour=#{ColorUtility.ToHtmlStringRGB(RenderSettings.fogColor)} fogMode={RenderSettings.fogMode} "
                      + $"fogDensity={RenderSettings.fogDensity} sun={(RenderSettings.sun != null ? RenderSettings.sun.name : "none")}");

        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/game-roots.txt", sb.ToString());
        Debug.Log("[menu] game roots report written");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }

    static void Walk(Transform t, int depth, StringBuilder sb)
    {
        string indent = new string(' ', depth * 2);
        List<string> parts = new List<string>();

        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null) { parts.Add("MISSING SCRIPT"); continue; }
            if (c is Transform) continue;
            parts.Add(c.GetType().Name);
        }

        string extra = "";

        if (t is RectTransform rt)
            extra += $" rect[{rt.anchorMin}-{rt.anchorMax} pos {rt.anchoredPosition} size {rt.sizeDelta}]";

        TMP_Text text = t.GetComponent<TMP_Text>();
        if (text != null)
            extra += $" text=\"{Trim(text.text)}\" font={(text.font != null ? text.font.name : "none")} {text.fontSize}pt";

        Image image = t.GetComponent<Image>();
        if (image != null)
            extra += $" sprite={(image.sprite != null ? image.sprite.name : "none")} colour=#{ColorUtility.ToHtmlStringRGBA(image.color)}";

        Menu menu = t.GetComponent<Menu>();
        if (menu != null)
            extra += $" MENU '{menu.menuName}'";

        sb.AppendLine($"{indent}{(t.gameObject.activeSelf ? "" : "(off) ")}{t.name}  <{string.Join(", ", parts)}>{extra}");

        Button button = t.GetComponent<Button>();
        if (button != null)
            Listeners(button.onClick, indent + "    onClick -> ", sb);

        TMP_InputField input = t.GetComponent<TMP_InputField>();
        if (input != null)
        {
            Listeners(input.onValueChanged, indent + "    onValueChanged -> ", sb);
            Listeners(input.onEndEdit, indent + "    onEndEdit -> ", sb);
        }

        foreach (Transform child in t)
            Walk(child, depth + 1, sb);
    }

    static void Listeners(UnityEventBase evt, string prefix, StringBuilder sb)
    {
        int count = evt.GetPersistentEventCount();
        if (count == 0)
            sb.AppendLine(prefix + "(nothing)");

        for (int i = 0; i < count; i++)
        {
            Object target = evt.GetPersistentTarget(i);
            sb.AppendLine($"{prefix}{Describe(target)}.{evt.GetPersistentMethodName(i)}");
        }
    }

    static string Describe(Object o)
    {
        if (o == null) return "null";
        if (o is Component c) return $"{PathOf(c.transform)} ({c.GetType().Name})";
        if (o is GameObject g) return $"{PathOf(g.transform)} (GameObject)";
        string path = AssetDatabase.GetAssetPath(o);
        return string.IsNullOrEmpty(path) ? $"{o.name} ({o.GetType().Name})" : $"{path} ({o.GetType().Name})";
    }

    static string PathOf(Transform t)
    {
        string path = t.name;
        while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
        return path;
    }

    static string Trim(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", "\\n");
        return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
    }
}
