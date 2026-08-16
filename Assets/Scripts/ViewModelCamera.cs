using UnityEngine;

/// <summary>
/// Stops the weapon and the world from cutting into each other.
///
/// Two separate problems arrive together when you stand against a wall in a first person game,
/// and they have different fixes.
///
/// The gun clipping through geometry is solved by drawing it with its own camera. The world
/// camera stops rendering the ViewModel layer entirely; a second camera draws only that layer,
/// on top, with the depth buffer cleared first. The weapon then physically cannot intersect
/// anything, because nothing else is in the buffer it is being tested against.
///
/// Seeing through the wall is the near clip plane. Unity's default is 0.3 metres, which means
/// anything closer than 30cm to your eye is simply not drawn - so pressing your face to a wall
/// puts the wall behind the near plane and you look straight through it. Pulled in to a few
/// centimetres, which is inside the character controller's own radius, so the wall is always
/// further away than the plane.
///
/// The second camera also needs a much narrower field of view than the world one. A weapon drawn
/// at ninety degrees looks like it is a hundred metres long; sixty is roughly what every shooter
/// uses for the same reason.
/// </summary>
public class ViewModelCamera : MonoBehaviour
{
    public const string LayerName = "ViewModel";

    [Tooltip("Field of view for the weapon alone. Lower than the world's, or the gun distorts.")]
    [SerializeField] float weaponFov = 55f;

    [Tooltip("How close to the eye something can be and still be drawn, in metres. Smaller than "
             + "the character controller's radius, so a wall is never inside it.")]
    [SerializeField] float nearClip = 0.02f;

    Camera world;
    Camera weapon;
    int layer;

    void Awake()
    {
        layer = LayerMask.NameToLayer(LayerName);

        if (layer < 0)
        {
            Debug.LogError($"[view] no '{LayerName}' layer - the weapon will keep clipping");
            enabled = false;
            return;
        }

        world = GetComponent<Camera>();

        if (world == null)
        {
            enabled = false;
            return;
        }

        world.nearClipPlane = nearClip;

        // The world camera stops drawing weapons entirely. Everything else it drew, it still
        // draws.
        world.cullingMask &= ~(1 << layer);

        GameObject host = new GameObject("~ViewModelCamera");
        host.transform.SetParent(transform, false);

        weapon = host.AddComponent<Camera>();
        weapon.clearFlags = CameraClearFlags.Depth;
        weapon.cullingMask = 1 << layer;
        weapon.nearClipPlane = 0.01f;
        weapon.farClipPlane = 12f;
        weapon.fieldOfView = weaponFov;

        // After the world camera, so it draws on top of a finished frame.
        weapon.depth = world.depth + 1;

        // No listener and no post processing on this one. Two AudioListeners make Unity complain
        // and mute one, and running the whole shader stack twice for a banana is wasteful.
        weapon.allowHDR = false;
        weapon.allowMSAA = false;
    }

    /// <summary>
    /// Puts a weapon on the layer the second camera draws, children included.
    ///
    /// Called whenever a loadout is built, because the weapon objects are created fresh each
    /// time and arrive on whatever layer their prefab carried.
    /// </summary>
    public static void Adopt(Transform root)
    {
        int layer = LayerMask.NameToLayer(LayerName);

        if (layer < 0 || root == null)
            return;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }
}
