using UnityEngine;

/// <summary>
/// A gorilla standing in the main menu's backdrop holding a banana gun - the key art, the way a
/// shooter's menu puts its soldiers in front of the map.
///
/// Built from the real player rig (MonkeyRig) and a real weapon (a GunInfo from Resources/Guns),
/// the same parts a remote player is made of in a match, so it's the actual character rather than
/// a stand-in. The rig and gun are built when the menu starts; this object is just where it stands
/// - move, turn and scale it in the scene to recompose the menu. The gizmo shows where it'll be.
/// </summary>
public class MenuGorilla : MonoBehaviour
{
    [Tooltip("Name of a GunInfo asset in Resources/Guns - Pistol, Rifle, Shotgun, Sniper, Pineapple, Peel.")]
    [SerializeField] string weapon = "Rifle";

    [SerializeField] bool tinted;
    [SerializeField] Color tint = new Color(1f, 0.85f, 0.1f);

    [Tooltip("Degrees the gorilla looks up or down (negative is up).")]
    [SerializeField] float lookPitch = 4f;

    // Same numbers PlayerController uses to put a remote player's weapon in their hand - see its
    // AttachWeaponsToHand for why the bone's 100x import scale has to be divided back out.
    [SerializeField] Vector3 handOffset = new Vector3(0.02f, 0f, 0.06f);
    [SerializeField] Vector3 handRotation = Vector3.zero;

    // The player capsule is 2 m tall on a centred pivot (see MonkeyRig's model placement).
    const float CapsuleHalfHeight = 1f;

    MonkeyRig rig;

    void Start()
    {
        GameObject body = new GameObject("~Gorilla");
        body.transform.SetParent(transform, false);

        // MonkeyRig puts the model a metre below its root - the root is a player capsule's
        // centre - so without this the gorilla stood buried to the chest in the floor.
        body.transform.localPosition = Vector3.up * CapsuleHalfHeight;

        rig = body.AddComponent<MonkeyRig>();

        if (!rig.Build(false))
        {
            Debug.LogWarning("[menu] couldn't build the menu gorilla's rig", this);
            Destroy(body);
            return;
        }

        shown = Wanted();
        rig.Tint(shown);

        rig.LookPitch = lookPitch;
        Arm(rig);
    }

    Color shown;

    void Update()
    {
        if (rig == null)
            return;

        // Checked every frame rather than on a property callback: it's one comparison, and it
        // catches every way the answer changes - picking a colour, joining or leaving a room, the
        // master handing out teams.
        Color wanted = Wanted();
        if (wanted != shown)
        {
            shown = wanted;
            rig.Tint(wanted);
        }
    }

    /// <summary>
    /// In a lobby, you - your colour, the way the lobby's backdrop already shows the map you've
    /// picked; your team's colour once a team mode has put you on one, since that's what you'll be
    /// drawn in. Reported: "when you change your gorilla's colour make that change be reflected in
    /// the background... instead of it being black". Outside a room there's no colour of yours to
    /// show, so it's the menu's own look.
    /// </summary>
    Color Wanted()
    {
        if (Photon.Pun.PhotonNetwork.InRoom && Photon.Pun.PhotonNetwork.LocalPlayer != null)
            return PlayerColours.For(Photon.Pun.PhotonNetwork.LocalPlayer);

        return tinted ? tint : Color.white;
    }

    void Arm(MonkeyRig built)
    {
        GunInfo info = Resources.Load<GunInfo>("Guns/" + weapon);
        Transform hand = built.RightHand;

        if (info == null || hand == null)
        {
            Debug.LogWarning($"[menu] no GunInfo '{weapon}' or no hand bone - the gorilla goes unarmed", this);
            return;
        }

        GameObject held = new GameObject(weapon);
        SingleShotGun gun = held.AddComponent<SingleShotGun>();
        gun.Configure(info, null, false);
        built.TwoHandedGrip = info.twoHanded;

        held.transform.SetParent(hand, false);
        Hitbox.Neutralise(held.transform);

        Vector3 scale = hand.lossyScale;
        held.transform.localPosition = new Vector3(
            Mathf.Approximately(scale.x, 0f) ? handOffset.x : handOffset.x / scale.x,
            Mathf.Approximately(scale.y, 0f) ? handOffset.y : handOffset.y / scale.y,
            Mathf.Approximately(scale.z, 0f) ? handOffset.z : handOffset.z / scale.z);
        held.transform.localRotation = Quaternion.Euler(handRotation);

        // A display piece: it never fires, so it shouldn't take input or play sounds.
        gun.enabled = false;
    }

    void OnDrawGizmos()
    {
        // Roughly the gorilla's footprint and height, so it can be placed before pressing Play.
        Gizmos.color = new Color(1f, 0.82f, 0.12f, 0.8f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, 0.95f, 0f), new Vector3(0.9f, 1.9f, 0.6f));
        Gizmos.DrawLine(new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.6f, 0.8f));
    }
}
