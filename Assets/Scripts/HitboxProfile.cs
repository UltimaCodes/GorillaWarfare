using UnityEngine;

/// <summary>
/// Hand-set radius for every hitbox, in metres. Replaces fitting them off the mesh.
///
/// The fitted approach measured how far real vertices sat from each bone segment and sized to
/// the 90th percentile - reasonable in principle, and wrong in practice for this rig, because it
/// trusts the mesh's own skin weights to say which part of the body a vertex belongs to. This
/// model's weights are painted broadly around the shoulder and hip joints - a lot of chest and
/// back skin is dominantly weighted to the shoulder bone rather than the spine - so even a fit
/// that only compares a vertex against segments in its own limb still measured the arm at 0.66m
/// radius, wider than the torso. That's not a fitting bug, it's the source data.
///
/// A number typed in by a person who can see the result is more reliable than a formula run
/// against data that turned out not to be trustworthy. Tune these here, then check them with
/// Tools/Gorilla Warfare/Photograph the hitboxes - it overlays exactly these values on the actual
/// mesh so a change can be seen before it's played.
/// </summary>
[CreateAssetMenu(menuName = "FPS/Hitbox Profile")]
public class HitboxProfile : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Must match a Part's 'from' bone name in Hitbox.cs exactly - Head, NECK, "
                 + "SPINE3, SPINE1, HIPS, LEFTHIP, RIGHTHIP, LEFTKNEE, RIGHTKNEE, LEFTSHOULDER, "
                 + "RIGHTSHOULDER, LEFTELBOW, RIGHTELBOW.")]
        public string bone;

        [Tooltip("For reading the list, not used for lookup.")]
        public string label;

        [Tooltip("Metres.")]
        [Range(0.02f, 1f)]
        public float radius = 0.15f;
    }

    public Entry[] entries = new Entry[0];

    /// The radius for a bone, or fallback if this profile has nothing for it - missing an entry
    /// should make a hitbox a little wrong, not make the player unhittable.
    public float RadiusFor(string bone, float fallback)
    {
        if (entries == null)
            return fallback;

        foreach (Entry e in entries)
        {
            if (e.bone == bone)
                return Mathf.Max(0.02f, e.radius);
        }

        return fallback;
    }
}
