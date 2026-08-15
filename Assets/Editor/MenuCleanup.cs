using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes the leftover AudioSource out of the menu scene.
//
// It has no clip on it, so on its own it plays nothing - but it's set to play on awake and
// loop, which means anything that ever assigns it a clip gets a second copy of the music
// running alongside the one MusicPlayer owns. Music belongs to MusicPlayer now; a spare source
// sitting in the scene is a trap rather than a feature.
public static class MenuCleanup
{
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Menu.unity", OpenSceneMode.Single);

        int removed = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                // Never touch a listener or anything a script is holding onto.
                Debug.Log($"[menu] removing AudioSource on '{source.gameObject.name}' "
                          + $"(clip: {(source.clip != null ? source.clip.name : "none")}, "
                          + $"playOnAwake: {source.playOnAwake})");

                // The object exists only to carry the source, so it goes too.
                if (source.gameObject.GetComponents<Component>().Length <= 2)
                    Object.DestroyImmediate(source.gameObject);
                else
                    Object.DestroyImmediate(source);

                removed++;
            }
        }

        if (removed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        Debug.Log($"[menu] {removed} AudioSource(s) removed");

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
