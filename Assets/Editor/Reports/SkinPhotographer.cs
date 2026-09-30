using System.Collections;
using System.IO;
using System.Text;
using Photon.Pun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Every weapon finish photographed on its turntable, in play mode so the glows move and the
/// auras have had time to stream - to look at them rather than trust the numbers. Also every
/// weapon in one finish (GW_SKIN_WEAPONS=key, default prismatic), and the crates' chests shut and
/// open. Output: Logs/skin-shots/*.png.
///
/// Unity -batchmode -projectPath . -executeMethod SkinPhotographer.Run   (no -quit)
/// </summary>
public static class SkinPhotographer
{
    const string Flag = "GorillaWarfare.SkinPhotographer";

    static string Folder => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "skin-shots");

    [MenuItem("Tools/Gorilla Warfare/Photograph the weapon finishes")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);
        PhotonNetwork.OfflineMode = true;
        new GameObject("~SkinPhotographer").AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            AudioListener.volume = 0f;
            Directory.CreateDirectory(Folder);
            foreach (string old in Directory.GetFiles(Folder, "*.png"))
                File.Delete(old);

            PreviewStage stage = PreviewStage.Create("photographer", 512, 384, new Color(0.08f, 0.09f, 0.11f));
            stage.spin = 25f;
            StringBuilder log = new StringBuilder("[skinshot]\n");

            int i = 0;
            foreach (WeaponFinish finish in FinishCatalog.All)
            {
                stage.ShowWeapon("Rifle", finish);
                stage.Turntable.localRotation = Quaternion.Euler(0f, -25f, 0f);
                yield return new WaitForSecondsRealtime(0.9f);
                Save(stage, $"{i:00}-{finish.rarity}-{finish.key}");
                log.AppendLine($"  {finish.rarity} {finish.key} '{finish.displayName}'");
                i++;
            }

            string key = System.Environment.GetEnvironmentVariable("GW_SKIN_WEAPONS");
            WeaponFinish across = FinishCatalog.Find(string.IsNullOrEmpty(key) ? "prismatic" : key);
            foreach (string weapon in WeaponLoadout.Everything)
            {
                stage.ShowWeapon(weapon, across);
                stage.Turntable.localRotation = Quaternion.Euler(0f, -25f, 0f);
                yield return new WaitForSecondsRealtime(0.7f);
                Save(stage, $"weapon-{weapon}");
            }

            stage.spin = 0f;
            foreach (string chest in new[] { "PirateChest", "PlatformerChest", "DungeonChest" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Art/Crates/{chest}/chest.fbx");
                GameObject shown = stage.Show(prefab, 200f);
                if (shown == null)
                {
                    log.AppendLine($"  no {chest}");
                    continue;
                }

                Animation anim = shown.GetComponentInChildren<Animation>();
                StringBuilder parts = new StringBuilder();
                foreach (Transform t in shown.GetComponentsInChildren<Transform>())
                    parts.Append(t.name).Append(' ');
                string clips = "";
                if (anim != null)
                    foreach (AnimationState state in anim)
                        clips += $"{state.name}({state.length:F2}s) ";
                log.AppendLine($"  {chest}: parts {parts} clips {clips}");

                yield return null;
                Save(stage, $"chest-{chest}-shut");

                if (anim != null && anim["open"] != null)
                {
                    anim.Play("open");
                    yield return new WaitForSecondsRealtime(anim["open"].length + 0.2f);
                }

                Save(stage, $"chest-{chest}-open");
            }

            Debug.Log(log.ToString());
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }

        static void Save(PreviewStage stage, string name)
        {
            stage.Camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = stage.Target;
            Texture2D shot = new Texture2D(stage.Target.width, stage.Target.height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0);
            shot.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), shot.EncodeToPNG());
            Destroy(shot);
        }
    }
}
