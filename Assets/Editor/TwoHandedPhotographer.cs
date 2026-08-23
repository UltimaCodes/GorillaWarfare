using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders a rig actually holding a weapon (the real SingleShotGun/AttachWeaponsToHand path, not
/// a stand-in guess) from a proper 3/4 or side angle, instead of PlayModeProbe's dead-on front
/// stand-in shot - which foreshortens any forward-reaching pose and isn't a fair angle to judge
/// "does this look like holding a rifle" from.
///
/// Needs Play Mode, not just a cold render: SingleShotGun.BuildVisual runs from Awake, and Awake
/// never fires on a component added outside Play Mode, since the script carries no
/// ExecuteInEditMode/ExecuteAlways attribute - PeelPhotographer/HitboxPhotographer sidestep this
/// by instantiating the raw weapon *model* prefab directly and never touching SingleShotGun at
/// all, but the whole point here is checking the real grip pose the real components build.
///
/// GW_GRIP_WEAPON picks the weapon (default Rifle); GW_GRIP_SIDE=1 renders a pure profile view
/// instead of the 3/4.
/// </summary>
public static class TwoHandedPhotographer
{
    const string Flag = "GorillaWarfare.TwoHandedPhotographer";

    public static string OutputPath =>
        Path.Combine(Application.dataPath, "..", "Library", "grip-check.png");

    [MenuItem("Tools/Gorilla Warfare/Photograph the grip")]
    public static void Run()
    {
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!SessionState.GetBool(Flag, false))
            return;

        SessionState.SetBool(Flag, false);
        new GameObject("~TwoHandedPhotographerRunner").AddComponent<Runner>();
    }

    class Runner : MonoBehaviour
    {
        IEnumerator Start()
        {
            string weaponName = System.Environment.GetEnvironmentVariable("GW_GRIP_WEAPON");
            if (string.IsNullOrEmpty(weaponName)) weaponName = "Rifle";

            GameObject stand = new GameObject("~GripStand");
            MonkeyRig rig = stand.AddComponent<MonkeyRig>();

            if (!rig.Build(false))
            {
                Debug.LogError("[grip] MonkeyRig.Build refused");
                EditorApplication.Exit(1);
                yield break;
            }

            Transform hand = rig.RightHand;
            if (hand == null)
            {
                Debug.LogError("[grip] no RightHand bone");
                EditorApplication.Exit(1);
                yield break;
            }

            GunInfo info = Resources.Load<GunInfo>("Guns/" + weaponName);
            if (info == null)
            {
                Debug.LogError($"[grip] no GunInfo at Guns/{weaponName}");
                EditorApplication.Exit(1);
                yield break;
            }

            // Built unparented first, matching PlayerController.AttachWeaponsToHand's own order -
            // the real path builds the loadout into itemHolder while it's still wherever it
            // started, then reparents the *built* container onto the hand bone afterward.
            GameObject held = new GameObject(weaponName);

            SingleShotGun gun = held.AddComponent<SingleShotGun>();
            gun.Configure(info, null, false);
            rig.TwoHandedGrip = info.twoHanded;

            // A frame for Awake (BuildVisual) to actually run.
            yield return null;

            held.transform.SetParent(hand, false);
            Hitbox.Neutralise(held.transform);

            GameObject prefab = Resources.Load<GameObject>("PhotonPrefabs/PlayerController");
            PlayerController template = prefab != null ? prefab.GetComponent<PlayerController>() : null;
            if (template != null)
            {
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object pos = typeof(PlayerController).GetField("weaponHandOffset", flags)?.GetValue(template);
                object rot = typeof(PlayerController).GetField("weaponHandRotation", flags)?.GetValue(template);

                // Same pre-division PlayerController.AttachWeaponsToHand does - the offset is
                // small and real-world scale, but the parent bone carries a 100x import scale
                // that multiplies localPosition regardless of the child's own (neutralised) scale.
                Vector3 boneScale = hand.lossyScale;

                if (pos is Vector3 localPos)
                {
                    held.transform.localPosition = new Vector3(
                        Mathf.Approximately(boneScale.x, 0f) ? localPos.x : localPos.x / boneScale.x,
                        Mathf.Approximately(boneScale.y, 0f) ? localPos.y : localPos.y / boneScale.y,
                        Mathf.Approximately(boneScale.z, 0f) ? localPos.z : localPos.z / boneScale.z);
                }

                if (rot is Vector3 localRot)
                    held.transform.localRotation = Quaternion.Euler(localRot);
            }

            // A couple of frames for the arm IK to settle onto the now-correctly-placed hand.
            yield return null;
            yield return null;

            Debug.Log($"[grip] {weaponName} twoHanded={info.twoHanded} hand.worldPos={hand.position} held.worldPos={held.transform.position}");

            GameObject camHost = new GameObject("~GripCam");
            Camera cam = camHost.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.18f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;

            bool side = System.Environment.GetEnvironmentVariable("GW_GRIP_SIDE") == "1";

            // Framed on the hand itself rather than the whole body's bounds - a bare test rig has
            // no CharacterController, and MonkeyRig.Build's model offset assumes one exists
            // (compensating for the capsule's pivot sitting at its middle), so a body-bounds frame
            // computed against this rig's raw, uncompensated position comes out badly centred.
            // The hand's own world position doesn't have that problem.
            Vector3 focus = hand.position;
            float dist = 2.2f;

            Vector3 eye = side
                ? focus + new Vector3(dist, 0.05f, 0f)
                : focus + new Vector3(dist * 0.75f, dist * 0.35f, -dist * 0.65f);

            camHost.transform.position = eye;
            camHost.transform.LookAt(focus, Vector3.up);

            GameObject lightHost = new GameObject("~GripLight");
            Light light = lightHost.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightHost.transform.rotation = Quaternion.Euler(40f, -25f, 0f);

            GameObject fillHost = new GameObject("~GripFill");
            Light fill = fillHost.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.4f;
            fillHost.transform.rotation = Quaternion.Euler(25f, 150f, 0f);

            yield return null;

            int size = 800;
            RenderTexture rt = new RenderTexture(size, size, 24);
            RenderTexture prevActive = RenderTexture.active;

            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D shot = new Texture2D(size, size, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            shot.Apply();
            RenderTexture.active = prevActive;

            File.WriteAllBytes(OutputPath, shot.EncodeToPNG());
            Debug.Log($"[grip] -> {OutputPath}");

            Object.Destroy(shot);
            rt.Release();
            Object.Destroy(rt);

            EditorApplication.Exit(0);
        }
    }
}
