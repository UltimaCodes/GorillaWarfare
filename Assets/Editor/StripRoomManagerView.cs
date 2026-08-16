using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Takes the PhotonView off RoomManager.
//
// RoomManager is DontDestroyOnLoad and also sits in the menu scene, so every trip back to the
// menu loads a second one. Its PhotonView is a *scene* view with a fixed ID, and two objects
// claiming view 999 is what produced this in Ryaan's console:
//
//     PhotonView ID duplicate found: 999 ... Maybe one wasn't destroyed on scene load?!
//     InvalidOperationException: Duplicate key 999
//
// RoomManager's own Awake destroys the duplicate, but a PhotonView registers itself before that
// happens, so the collision is thrown either way - and an exception during scene load takes
// whatever was queued behind it down too, which is why the game came back in pieces.
//
// The view was never needed. RoomManager declares no RPCs and does not implement
// IPunObservable; it spawns players with PhotonNetwork.Instantiate, which needs a view on the
// prefab, not on whatever asked for it.
public static class StripRoomManagerView
{
    const string ScenePath = "Assets/Scenes/Menu.unity";

    [MenuItem("Tools/Gorilla Warfare/Strip the RoomManager PhotonView")]
    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int removed = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (RoomManager manager in root.GetComponentsInChildren<RoomManager>(true))
            {
                foreach (PhotonView view in manager.GetComponents<PhotonView>())
                {
                    Debug.Log($"[net] removing PhotonView {view.ViewID} from {manager.name} - "
                              + "it has no RPCs and no observed components");

                    Object.DestroyImmediate(view, true);
                    removed++;
                }
            }
        }

        if (removed == 0)
        {
            Debug.Log("[net] RoomManager already has no PhotonView");
        }
        else
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[net] removed {removed} - the duplicate view ID on scene reload is gone");
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(0);
    }
}
