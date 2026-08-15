using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Photon.Realtime;
using UnityEngine.UI;

public class PlayerController : MonoBehaviourPunCallbacks, IDamageable, IPunObservable
{
    [SerializeField] float mouseSensitivity = 3f;
    [SerializeField] GameObject cameraHolder;
    // Populated at runtime by WeaponLoadout. Left serialized so the prefab's old entries are
    // visible, but they're replaced on spawn.
    [SerializeField] Item[] items;
    [SerializeField] Image healthbarImage;
    [SerializeField] GameObject ui;

    // The camera sits at (0, 0.5, 0.303) inside CameraHolder while ItemHolder is at its origin,
    // so a weapon spawned at zero lands below and behind the camera, inside the near clip plane.
    // That's why nobody could see their own gun. This puts it down and to the right of the eye,
    // which is where a first person weapon lives.
    [Header("First person weapon placement")]
    // Worked out from the camera, not guessed. The camera sits at y=0.5 inside CameraHolder,
    // so a holder at y=0.26 put everything ~0.44 below the eye at 0.75 forward - about 30
    // degrees down, which is the bottom edge of a 60 degree FOV. That's why the hands were
    // invisible: they were rendering just off the bottom of the screen.
    // This sits the weapon ~17 degrees below and ~12 right of centre, well inside the frame.
    [SerializeField] Vector3 weaponViewOffset = new Vector3(0.18f, 0.24f, 0.85f);
    // Angled across the view rather than pointing straight down the camera axis. Aimed
    // straight ahead you see a long thin banana end-on, which reads as a tube - you need the
    // yaw to show its curve and silhouette, which is how CS frames a rifle.
    [SerializeField] Vector3 weaponViewRotation = new Vector3(-4f, -14f, 7f);
    // Arms are built running back from the weapon, so they sit behind the holder and slightly
    // in from it - the hands meet the banana, the elbows disappear off the bottom.
    [SerializeField] Vector3 viewArmsOffset = new Vector3(-0.02f, 0.02f, -0.24f);
    [SerializeField] Vector3 viewArmsRotation = new Vector3(4f, 12f, -7f);

    [Header("Third person weapon placement")]
    [SerializeField] Vector3 weaponHandOffset = new Vector3(0.02f, 0f, 0.06f);
    [SerializeField] Vector3 weaponHandRotation = new Vector3(0f, 0f, 0f);
    int itemIndex;
    int previousItemIndex = -1;
    float verticalLookRotation;
    float horizontalLookRotation;

    // Recoil is kept separate from the look angles and added on top, so recovery can pull it
    // back without fighting the mouse. Pull down while firing and you're cancelling this, which
    // is exactly the skill the pattern is there to teach.
    Vector2 recoilOffset;
    Vector2 recoilTarget;
    float recoilRecovery = 0.75f;
    float recoilSpeed = 6f;
    float lastRecoilAt = -1f;

    // Grace period after the last shot before the view starts returning.
    const float recoilHoldTime = 0.18f;
    bool cursorLocked = true;
    const float maxHealth = 100f;
    float currentHealth = maxHealth;

    // Set the moment health hits zero, cleared by respawning into a fresh controller. Guards
    // against a burst landing after the killing shot and being counted as a second kill.
    bool dead;
    WeaponLoadout loadout;

    // Pitch lives on cameraHolder, which nothing replicates - we send it ourselves.
    float remoteVerticalLook;
    const float pitchLerpSpeed = 15f;

    // Camera on the prefab is Untagged so Camera.main is useless. Billboards need this.
    public static Camera LocalCamera { get; private set; }

    /// Hands the static over to the death camera while the controller is gone, so nameplates
    /// keep facing the right way instead of hunting for a camera that no longer exists.
    public static void SetLocalCamera(Camera camera) => LocalCamera = camera;


    /// Replicated so every client builds the same weapons for this player. Gun game rewrites
    /// it as you climb the ladder; deathmatch sets it once per match.
    public const string LoadoutKey = "guns";
    public const string ItemIndexKey = "itemIndex";

    PhotonView PV;
    MonkeyRig rig;
    Transform itemHolder;

    public PhotonView View => PV;

    /// What the HUD needs to draw itself.
    public Vector2 RecoilOffset => recoilOffset;
    public SingleShotGun ActiveGun =>
        items != null && itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] as SingleShotGun : null;

    public CombatHud Hud { get; private set; }

    void Awake()
    {
        PV = GetComponent<PhotonView>();

        // Resolved once. It was being found by walking every transform on the player, from
        // four separate call sites, on an object that respawns every time you die.
        itemHolder = FindItemHolder();

        if (itemHolder == null)
            Debug.LogError("No ItemHolder on the player - there is nowhere to put a weapon.", this);
    }

    void Start()
    {
        // Added here rather than on the prefab so there's nothing to wire up. Runs on remote
        // players too, since their transforms are replicated - so you hear their steps.
        gameObject.AddComponent<FootstepPlayer>();

        // Hidden from its owner - you shouldn't see your own body from inside its head - but it
        // still casts a shadow.
        rig = gameObject.AddComponent<MonkeyRig>();
        if (!rig.Build(PV.IsMine))
        {
            Destroy(rig);
            rig = null;
        }

        // The movement capsule goes on Player, which weapons don't trace against. Otherwise the
        // capsule is a single volume around the whole body and every part of a player is worth
        // the same - there'd be nowhere to aim.
        int playerLayer = LayerMask.NameToLayer(Hitbox.PlayerLayerName);
        if (playerLayer >= 0)
            gameObject.layer = playerLayer;

        if (rig != null)
        {
            int boxes = Hitbox.BuildFor(transform, this);
            if (boxes == 0)
                Debug.LogError("No hitboxes built - this player cannot be shot.", this);
        }

        if (PV.IsMine)
        {
            LocalCamera = GetComponentInChildren<Camera>();
            gameObject.AddComponent<PlayerMovement>();
            PlaceViewModel();

            Hud = gameObject.AddComponent<CombatHud>();
            Hud.Bind(this);

            // Sway goes on the item holder rather than the camera, so it moves the weapon
            // without moving where you're aiming.
            if (itemHolder != null)
                itemHolder.gameObject.AddComponent<WeaponSway>();
        }
        else
        {
            Camera ownCamera = GetComponentInChildren<Camera>();
            if (ownCamera != null)
                Destroy(ownCamera.gameObject);

            // Remote copies are driven by PhotonTransformView, so nothing local should be
            // moving them. Killing PlayerMovement is enough - a CharacterController you never
            // call Move() on does nothing on its own.
            //
            // Do NOT disable the CharacterController here. It derives from Collider, and a
            // disabled collider is skipped by every physics query, so remote players had no
            // hitbox at all and every shot went straight through them.
            if (TryGetComponent(out PlayerMovement movement))
                Destroy(movement);

            if (ui != null)
                Destroy(ui);

            // Weapons hang off CameraHolder, which is a first person position - to everyone else
            // that's floating in the middle of the body. Move them onto the hand.
            AttachWeaponsToHand();
        }

        // Both copies. Everyone needs the models - it is how you tell what someone is holding -
        // and only the owner's are allowed to fire. This used to run for the owner alone, so
        // remote players were left carrying whatever the prefab happened to ship with.
        BuildLoadout();

        // After the loadout, never before. Building a loadout clears weapons out of the holder,
        // and the arms live in that same holder - built first, they survived exactly one frame,
        // which is why the hands were never visible no matter where they were positioned.
        if (PV.IsMine)
            BuildViewArms();
    }

    void OnDestroy()
    {
        // Respawning recreates the controller, so don't leave a dead camera in the static.
        if (LocalCamera != null && PV != null && PV.IsMine)
            LocalCamera = null;
    }



    void Update()
    {
        if (!PV.IsMine)
        {
            ApplyRemoteLook();
            FeedRig();
            return;
        }

        UpdateCursorLock();
        FeedRig();

        // The match is over and the scoreboard is up. Aiming and firing through it looks like
        // the game failed to notice it had ended.
        if (MatchState.Phase == MatchPhase.Over)
            return;

        Look();

        // Gun game hands you exactly one weapon and can leave you briefly holding nothing while
        // a rebuild lands, so none of the input below may assume there's something in your hands.
        if (items == null || items.Length == 0)
        {
            FallOutOfTheWorldCheck();
            return;
        }

        // Stowed weapons are deactivated so their own Update never runs. Without this a reload
        // started before switching would sit frozen until you came back to it.
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is SingleShotGun stowed && !stowed.gameObject.activeInHierarchy)
                stowed.TickReloadWhileStowed();
        }

        // Number keys, from a table rather than (i + 1).ToString() - that built a string per
        // weapon per frame purely to ask whether a key was down.
        int keys = Mathf.Min(items.Length, WeaponKeys.Length);
        for (int i = 0; i < keys; i++)
        {
            if (Input.GetKeyDown(WeaponKeys[i]))
            {
                EquipItem(i);
                break;
            }
        }

        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0f)
            EquipItem(itemIndex >= items.Length - 1 ? 0 : itemIndex + 1);
        else if (scroll < 0f)
            EquipItem(itemIndex <= 0 ? items.Length - 1 : itemIndex - 1);

        Item held = items[Mathf.Clamp(itemIndex, 0, items.Length - 1)];

        if (Input.GetMouseButtonDown(0))
            held.Use();
        else if (Input.GetMouseButton(0) && held is SingleShotGun heldGun)
            heldGun.UseHeld();

        if (Input.GetKeyDown(KeyCode.R) && held is SingleShotGun reloadGun)
            reloadGun.Reload();

        FallOutOfTheWorldCheck();
    }


    // The map has no floor past the edges, and falling forever isn't a death state anyone
    // enjoys watching.
    void FallOutOfTheWorldCheck()
    {
        if (transform.position.y < -10f)
            Die(null, "the void", false);
    }

    static readonly KeyCode[] WeaponKeys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
    };

    void EquipItem(int index)
    {
        // The index arrives over the network, and for a while a remote copy carried whatever
        // the prefab shipped with while its owner carried a full loadout - so this was handed
        // a 3 for a two element array and threw on every weapon switch, on every other client.
        // PUN dispatches callbacks in a bare foreach with no try/catch, so that also silently
        // dropped the update for every callback target queued behind this one.
        if (items == null || items.Length == 0 || index < 0 || index >= items.Length)
            return;

        if (index == previousItemIndex)
            return;

        itemIndex = index;
        items[itemIndex].itemGameObject.SetActive(true);

        if (previousItemIndex >= 0 && previousItemIndex < items.Length)
            items[previousItemIndex].itemGameObject.SetActive(false);

        previousItemIndex = itemIndex;

        if (PV.IsMine)
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ItemIndexKey, itemIndex } });
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (targetPlayer != PV.Owner)
            return;

        // A new loadout means new weapon objects on every client, owner included - this is how
        // gun game moves you up a rung without respawning you.
        if (changedProps.ContainsKey(LoadoutKey))
        {
            BuildLoadout();
            return;
        }

        if (changedProps.ContainsKey(ItemIndexKey) && !PV.IsMine)
            EquipItem((int)changedProps[ItemIndexKey]);
    }


    // verticalLookRotation is maintained on both sides - Look() sets it locally, ApplyRemoteLook
    // lerps it toward the replicated value - so the rig reads the same field either way.
    void FeedRig()
    {
        if (rig != null)
            rig.LookPitch = verticalLookRotation;
    }

    /// Moves the item holder into view for the owner. Without this the weapon sits at the
    /// holder's origin, which is behind the camera.
    void PlaceViewModel()
    {
        if (itemHolder == null)
            return;

        itemHolder.localPosition = weaponViewOffset;
        itemHolder.localRotation = Quaternion.Euler(weaponViewRotation);
    }

    // First person arms. The owner's real body is ShadowsOnly - you can't show a full gorilla
    // from inside its own head - so this is a separate pair of arms parented to the weapon
    // holder, which is how basically every FPS does it. They sway with the gun because they're
    // children of it.
    void BuildViewArms()
    {
        if (itemHolder == null)
            return;

        GameObject prefab = Resources.Load<GameObject>("Models/Weapons/ViewArms");
        if (prefab == null)
        {
            Debug.LogWarning("No view arms model - you won't see your hands.", this);
            return;
        }

        GameObject arms = Instantiate(prefab, itemHolder);
        arms.transform.localPosition = viewArmsOffset;
        arms.transform.localRotation = Quaternion.Euler(viewArmsRotation);

        Material mat = Resources.Load<Material>("Models/Weapons/ViewArmsMat");
        if (mat != null)
        {
            foreach (Renderer r in arms.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        }
    }

    Transform FindItemHolder()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "ItemHolder")
                return t;
        }

        return null;
    }

    // Only for remote copies. The owner keeps their weapon on the camera, because that's what
    // makes a first person gun feel attached to the view rather than to a character.
    void AttachWeaponsToHand()
    {
        if (rig == null || rig.RightHand == null || itemHolder == null)
            return;

        itemHolder.SetParent(rig.RightHand, false);
        itemHolder.localPosition = weaponHandOffset;
        itemHolder.localRotation = Quaternion.Euler(weaponHandRotation);
    }

    /// Which weapons a player is carrying, as every client sees it. The list is a replicated
    /// custom property rather than a local decision, because the copy of you on someone else's
    /// screen has to build exactly the same weapons in the same order - that index is what
    /// gets replicated when you switch.
    public static string[] LoadoutFor(Player player)
    {
        if (player != null
            && player.CustomProperties.TryGetValue(LoadoutKey, out object value)
            && value is string names
            && !string.IsNullOrEmpty(names))
        {
            return MatchState.Rules.Deserialise(names);
        }

        // No match running, or the property has not arrived yet. Everything, which is what it
        // did before gamemodes existed - and a rebuild follows the moment the property lands.
        return WeaponLoadout.AllWeapons;
    }

    /// Publishes what the local player is carrying. Only the owner may call this.
    public static void PublishLoadout(IEnumerable<string> weapons)
    {
        PhotonNetwork.LocalPlayer.SetCustomProperties(
            new Hashtable { { LoadoutKey, MatchState.Rules.Serialise(weapons) } });
    }

    void BuildLoadout()
    {
        if (itemHolder == null)
            return;

        if (loadout == null)
            loadout = gameObject.AddComponent<WeaponLoadout>();

        // The owner traces shots and so needs the camera; nobody else may fire at all.
        List<SingleShotGun> guns = loadout.Build(itemHolder, PV.IsMine ? LocalCamera : null,
                                                 LoadoutFor(PV.Owner), PV.IsMine);

        items = new Item[guns.Count];
        for (int i = 0; i < guns.Count; i++)
            items[i] = guns[i];

        // Rebuilding replaces every weapon object, so whatever was equipped is gone. Remote
        // copies re-read the owner's replicated index rather than snapping back to the first
        // weapon, otherwise a gun game rebuild would show the wrong banana until you switched.
        previousItemIndex = -1;
        EquipItem(PV.IsMine ? 0 : StatOf(PV.Owner, ItemIndexKey));
    }

    static int StatOf(Player player, string key)
    {
        return player != null && player.CustomProperties.TryGetValue(key, out object value) && value is int i
            ? i
            : 0;
    }

    /// Weapons report their shots through here so they don't each need a PhotonView of their
    /// own. That's what lets a loadout be spawned at runtime - allocating view IDs for
    /// dynamically created objects is a mess, and nothing about a gunshot actually needs its
    /// own networked identity.
    public void ReportShot(string weaponName, Vector3 hitPoint, Vector3 hitNormal)
    {
        PV.RPC(nameof(RPC_WeaponFired), RpcTarget.All, weaponName, hitPoint, hitNormal);
    }

    [PunRPC]
    void RPC_WeaponFired(string weaponName, Vector3 hitPoint, Vector3 hitNormal)
    {
        GameAudio.PlayAt($"{GameAudio.Shoot}/{weaponName}", transform.position, 0.6f);
        GameAudio.PlayAt(GameAudio.Impact, hitPoint, 0.5f);

        // Flash and decal on the weapon that actually fired, so it's right for spectators too.
        foreach (SingleShotGun gun in GetComponentsInChildren<SingleShotGun>(true))
        {
            if (gun.name == weaponName)
            {
                gun.PlayFireEffects(hitPoint, hitNormal);
                break;
            }
        }
    }

    /// Called by a weapon on each shot. Kick is (pitch, yaw) in degrees.
    public void AddRecoil(Vector2 kick, float recovery, float speed)
    {
        recoilTarget += kick;
        recoilRecovery = recovery;
        recoilSpeed = speed;
        lastRecoilAt = Time.time;
    }

    void Look()
    {
        horizontalLookRotation += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        verticalLookRotation -= Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);

        UpdateRecoil();

        // Recoil rides on top of the look angles rather than being folded into them, so
        // recovering doesn't undo where you actually pointed the mouse.
        transform.localEulerAngles = new Vector3(0f, horizontalLookRotation + recoilOffset.y, 0f);
        cameraHolder.transform.localEulerAngles = new Vector3(verticalLookRotation - recoilOffset.x, 0f, 0f);
    }

    void UpdateRecoil()
    {
        // Recovery only starts once you're off the trigger. Decaying while you fire made the
        // spray self limiting - it climbed a little, then the decay matched the kick and it
        // stopped going anywhere, which is why it felt like there was nothing to fight. A spray
        // should keep climbing until you pull down against it.
        if (Time.time - lastRecoilAt > recoilHoldTime)
            recoilTarget = Vector2.Lerp(recoilTarget, Vector2.zero, recoilRecovery * recoilSpeed * Time.deltaTime);

        recoilOffset = Vector2.Lerp(recoilOffset, recoilTarget, 1f - Mathf.Exp(-recoilSpeed * 2.5f * Time.deltaTime));
    }

    // Lerped, not snapped - serialization only fires 20x/sec so raw values visibly step.
    // cameraHolder survives on remote clients; Start only kills the Camera child.
    void ApplyRemoteLook()
    {
        if (cameraHolder == null)
            return;

        verticalLookRotation = Mathf.LerpAngle(verticalLookRotation, remoteVerticalLook, Time.deltaTime * pitchLerpSpeed);
        cameraHolder.transform.localEulerAngles = new Vector3(verticalLookRotation, 0f, 0f);
    }

    // One float on the existing root view, rather than a whole extra PhotonView on cameraHolder.
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(verticalLookRotation);
        }
        else
        {
            remoteVerticalLook = (float)stream.ReceiveNext();
        }
    }



    void UpdateCursorLock()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            cursorLocked = false;
        }
        else if (Input.GetMouseButtonDown(0))
        {
            cursorLocked = true;
        }

        if (cursorLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void TakeDamage(float damage, string weapon, bool headshot)
    {
        PV.RPC(nameof(RPC_TakeDamage), PV.Owner, damage, weapon, headshot);
    }

    [PunRPC]
    void RPC_TakeDamage(float damage, string weapon, bool headshot, PhotonMessageInfo info)
    {
        // Already dead and waiting to respawn. Without this a burst that lands after the
        // killing shot credits a second kill for the same death.
        if (dead)
            return;

        currentHealth -= damage;

        if (healthbarImage != null)
            healthbarImage.fillAmount = currentHealth / maxHealth;

        // 2D - this happened to you, not near you.
        GameAudio.Play2D(GameAudio.Hurt, 0.7f, 0.1f);

        if (currentHealth <= 0f)
            Die(info.Sender, weapon, headshot);
    }

    /// killer is null for falling off the map and anything else self inflicted.
    void Die(Player killer, string weapon, bool headshot)
    {
        if (dead)
            return;

        dead = true;

        // Only the victim runs this - it is the one client that knows the health hit zero - so
        // it has to tell everyone else. Before this nobody but you knew you had died, which
        // left the kill feed and the match itself with nothing to listen to.
        int killerActor = killer != null ? killer.ActorNumber : -1;
        PV.RPC(nameof(RPC_Died), RpcTarget.All, killerActor, weapon ?? string.Empty, headshot);

        if (RoomManager.Instance != null)
            RoomManager.Instance.HandleLocalDeath(transform.position, transform.forward);
    }

    [PunRPC]
    void RPC_Died(int killerActor, string weapon, bool headshot)
    {
        GameAudio.PlayAt(GameAudio.Death, transform.position, 0.8f);

        Player killer = killerActor >= 0 && PhotonNetwork.InRoom
            ? PhotonNetwork.CurrentRoom.GetPlayer(killerActor)
            : null;

        // Everyone gets the kill feed entry; only the master acts on the score, so two clients
        // can never disagree about it and a late joiner reads the same numbers as everyone else.
        MatchState.ReportKill(killer, PV.Owner, weapon, headshot);
    }
}
