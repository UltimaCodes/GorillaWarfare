using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Photon.Realtime;

public class PlayerController : MonoBehaviourPunCallbacks, IDamageable, IPunObservable
{
    [SerializeField] float mouseSensitivity = 3f;
    [SerializeField] GameObject cameraHolder;
    // Populated at runtime by WeaponLoadout. Left serialized so the prefab's old entries are
    // visible, but they're replaced on spawn.
    [SerializeField] Item[] items;

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
    [SerializeField] Vector3 weaponViewOffset = new Vector3(0.4f, 0.26f, 0.8f);
    // Angled across the view rather than pointing straight down the camera axis. Aimed
    // straight ahead you see a long thin banana end-on, which reads as a tube - you need the
    // yaw to show its curve and silhouette, which is how CS frames a rifle.
    [SerializeField] Vector3 weaponViewRotation = new Vector3(-4f, -14f, 8f);

    [SerializeField] float aimSpeed = 12f;

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

    /// <summary>
    /// 140 rather than 100, which is the same thing as cutting every weapon's damage by 40%
    /// but expressed as one number instead of five.
    ///
    /// Doing it this way keeps the relationships between the weapons exactly as they were -
    /// the rifle still wins on sustained damage, the shotgun is still devastating up close,
    /// the sniper still takes a head off - so none of the balance work has to be redone and
    /// WeaponCheck's assertions about roles all still hold. Editing five damage figures by
    /// hand would have drifted every one of those.
    ///
    /// What it buys: time to kill goes from 0.40s to 0.60-0.80s across the board, and the
    /// shotgun stops being a one-shot. At 108 damage in a single trigger pull it killed a full
    /// health player instantly, which is not a fight, it's a coin toss on who saw who first.
    /// </summary>
    const float maxHealth = 140f;

    float currentHealth = maxHealth;

    // Killing someone puts you back on your feet.
    //
    // The complaint was dying too fast, and more health alone only delays that - you still
    // grind down over a match with no way back up short of dying. Healing on a kill rewards
    // winning a fight by letting you stay in the next one, which is what makes a room full of
    // people feel like a brawl instead of a series of trades.
    //
    // It also scales with a streak, so the person doing well gets to keep doing well and
    // everyone else gets an obvious target.
    const float healPerKill = 35f;
    const float healPerStreak = 12f;
    const float maxStreakHeal = 36f;

    /// <summary>
    /// The ceiling a killstreak can push you past your own maximum.
    ///
    /// Healing does nothing for someone already at full health, which is exactly the person a
    /// streak is supposed to reward - so anything left over becomes overshield instead. It sits
    /// on top of normal health and comes off first.
    ///
    /// 200 against a base of 140, so a streak is worth up to 60 extra - about 43% more life.
    /// It was 150, which bought +10 and may as well not have existed.
    ///
    /// What that means in a fight: a fully shielded player takes six pistol shots instead of
    /// four, or ten rifle rounds instead of seven. Enough that whoever is on a run is genuinely
    /// harder to put down, without making them unkillable - two sniper headshots still do it.
    /// </summary>
    const float overshieldCeiling = 200f;

    /// Consecutive kills without dying. Read by the HUD.
    public int Killstreak { get; private set; }

    // Consecutive hits, and how long you have to land the next one before it lapses.
    //
    // Separate from the killstreak on purpose - a streak is about winning fights, a combo is
    // about the seconds inside one. The combo is what makes a shotgun blast or a held rifle
    // burst climb while it happens rather than paying out only at the end.
    const float comboWindow = 1.4f;

    int combo;
    float comboLapsesAt;

    /// How many hits you've landed back to back. The HUD draws it; the hit sound rides it.
    public int Combo => Time.time < comboLapsesAt ? combo : 0;

    /// <summary>
    /// Called for every shot that connects. Returns where in the combo this hit sits, which
    /// the weapon turns into a pitch.
    /// </summary>
    public int RegisterHit()
    {
        combo = Time.time < comboLapsesAt ? combo + 1 : 1;
        comboLapsesAt = Time.time + comboWindow;

        if (Hud != null)
            Hud.ShowCombo(combo);

        return combo;
    }

    // Multikills - kills close enough together to be one moment rather than two.
    const float multikillWindow = 4f;

    int multikill;
    float multikillLapsesAt;

    /// The local player, for anything that needs to reach them from elsewhere - a kill is
    /// reported on the victim's view, so healing the killer means finding them.
    public static PlayerController Local { get; private set; }

    // Set the moment health hits zero, cleared by respawning into a fresh controller. Guards
    // against a burst landing after the killing shot and being counted as a second kill.
    bool dead;
    WeaponLoadout loadout;

    // Whatever the camera was set to on the prefab, so aiming has something to return to.
    float baseFov = 60f;
    float aimSensitivity = 1f;
    WeaponSway sway;

    // Pitch lives on cameraHolder, which nothing replicates - we send it ourselves.
    float remoteVerticalLook;
    const float pitchLerpSpeed = 15f;

    // Camera on the prefab is Untagged so Camera.main is useless. Billboards need this.
    public static Camera LocalCamera { get; private set; }

    /// Whether the game currently wants the mouse. Exposed because Cursor.lockState cannot be
    /// read back meaningfully without a graphics device, so the play mode probe has no other
    /// way to tell whether the game intends to hold the cursor.
    public static bool CursorCaptured { get; private set; }

    /// <summary>
    /// Clears everything static that describes a match.
    ///
    /// These outlive the scene, which is the whole point of them - but that also means a match
    /// that has ended leaves its leftovers pointing at destroyed objects, and whatever reads
    /// them next gets a null or a stale answer. Cheaper to have one place that forgets than to
    /// have every reader defend itself.
    /// </summary>
    public static void ForgetLocals()
    {
        Local = null;
        LocalCamera = null;
        CursorCaptured = false;
        AimInputOverride = null;
    }

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
    public SingleShotGun ActiveGun =>
        items != null && itemIndex >= 0 && itemIndex < items.Length ? items[itemIndex] as SingleShotGun : null;

    public GameHud Hud { get; private set; }

    /// The last rung this player was told about, so a promotion is announced once rather than on
    /// every kill. Starts at zero because that is where everybody starts.
    int announcedRung;

    /// <summary>
    /// Holds the aim button on behalf of a test. Null means read the mouse as normal.
    ///
    /// Batch mode has no mouse, and driving UpdateAim directly doesn't work either - the real
    /// Update calls it again the same frame with the button released, so the two fight and the
    /// transition stalls halfway. Overriding the input instead exercises the actual path.
    /// </summary>
    public static bool? AimInputOverride { get; set; }

    /// True while the right mouse button is down on a weapon that can aim. Read by the weapon
    /// for its spread, and by the HUD to get the crosshair out of the way.
    public bool IsAiming { get; private set; }

    /// 0 to 1. The HUD draws it; the old screen space healthbar on the prefab is gone.
    public float HealthFraction => Mathf.Clamp01(currentHealth / maxHealth);
    public int HealthPoints => Mathf.Max(0, Mathf.CeilToInt(currentHealth));

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

        // Both of these belong to every copy, which is why they are here rather than in
        // AttachWeaponsToHand - that runs for remote players only, and stamping the spawn time
        // there meant the one player whose immunity actually matters never had any.
        ApplyColour();

        spawnedAt = Time.time;
        gaveUpProtection = false;

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
            Local = this;
            LocalCamera = GetComponentInChildren<Camera>();

            if (LocalCamera != null)
            {
                // The camera is built fresh on every respawn and arrives holding whatever the
                // prefab was authored with, so the saved field of view has to be reapplied
                // rather than read once at startup.
                GameSettings.ApplyFov();
                baseFov = LocalCamera.fieldOfView;
            }

            gameObject.AddComponent<PlayerMovement>();

            // Only on your own body. Wind lines around somebody else's gorilla would be drawn
            // in their peripheral vision, not yours.
            gameObject.AddComponent<SpeedRush>();
            PlaceViewModel();

            // The HUD is a scene object now rather than something bolted onto the player, so
            // this asks the scene for it instead of creating one. It outlives you: you die and
            // respawn several times a match and the ammo counter shouldn't be rebuilt each
            // time, and it has to keep drawing the respawn timer while there's no player at all.
            Hud = GameHud.Instance;

            if (Hud != null)
                Hud.Bind(this);
            else
                Debug.LogWarning("[player] no GameHud in the scene - "
                                 + "run Tools/Gorilla Warfare/Build the in-game HUD");

            // Sway goes on the item holder rather than the camera, so it moves the weapon
            // without moving where you're aiming. It's told where the holder is rather than
            // reading it once at Start, because aiming moves that and the two would otherwise
            // fight over the same transform every frame.
            if (itemHolder != null)
                sway = itemHolder.gameObject.AddComponent<WeaponSway>();
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

            // Weapons hang off CameraHolder, which is a first person position - to everyone else
            // that's floating in the middle of the body. Move them onto the hand.
            AttachWeaponsToHand();
        }

        // No rolling your own weapon here any more. The master issues every loadout - on join,
        // on climbing a rung, and on dying in deathmatch - so there is one client deciding what
        // anybody carries. Publishing from here as well meant two writers to one property, and
        // a late joiner's spawn would land on top of the loadout the master had just sent them.

        // Both copies. Everyone needs the models - it is how you tell what someone is holding -
        // and only the owner's are allowed to fire. This used to run for the owner alone, so
        // remote players were left carrying whatever the prefab happened to ship with.
        BuildLoadout();
    }

    void OnDestroy()
    {
        // Respawning recreates the controller, so don't leave a dead camera in the static.
        if (LocalCamera != null && PV != null && PV.IsMine)
            LocalCamera = null;

        if (Local == this)
            Local = null;
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

        UpdateAim(AimInputOverride ?? KeyBinds.Held(KeyBinds.Action.Aim));

        // Not while the settings screen is up. The mouse is being used to drag sliders, and
        // reading it as look input meant the camera span round behind the panel while you
        // adjusted your sensitivity - which is a memorable way to discover a new sensitivity.
        if (cursorLocked)
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

        // Nothing below this line happens while the settings screen is up. The guard used to sit
        // further down, under the weapon cycling, so scrolling a list of key bindings also
        // scrolled through your weapons - which is the same leak as the camera and the trigger,
        // one line higher up than I put the fix.
        if (!cursorLocked)
        {
            FallOutOfTheWorldCheck();
            return;
        }

        // Cycling rather than number keys. Both modes hand out a single weapon now, so slots
        // one through five addressed a rack that no longer exists - and the two people who
        // still want to flick between things want it bound to something they chose.
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");

        if (scroll > 0f || KeyBinds.Pressed(KeyBinds.Action.NextWeapon))
            EquipItem(itemIndex >= items.Length - 1 ? 0 : itemIndex + 1);
        else if (scroll < 0f || KeyBinds.Pressed(KeyBinds.Action.PreviousWeapon))
            EquipItem(itemIndex <= 0 ? items.Length - 1 : itemIndex - 1);

        Item held = items[Mathf.Clamp(itemIndex, 0, items.Length - 1)];

        if (KeyBinds.Pressed(KeyBinds.Action.Fire))
        {
            DropProtection();
            held.Use();
        }
        else if (KeyBinds.Held(KeyBinds.Action.Fire) && held is SingleShotGun heldGun)
        {
            DropProtection();
            heldGun.UseHeld();
        }

        if (KeyBinds.Pressed(KeyBinds.Action.Reload) && held is SingleShotGun reloadGun)
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

        // Switching away while scoped left the old weapon's renderers off, so it came back
        // invisible next time you drew it.
        if (items[itemIndex] is SingleShotGun drawn)
            drawn.SetVisible(true);

        if (previousItemIndex >= 0 && previousItemIndex < items.Length)
            items[previousItemIndex].itemGameObject.SetActive(false);

        previousItemIndex = itemIndex;

        if (PV.IsMine)
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ItemIndexKey, itemIndex } });
    }

    /// <summary>
    /// Paints this gorilla whatever colour it should be right now.
    ///
    /// Called on spawn and again whenever the colour or the team changes, because both can move
    /// underneath a player who is already standing there - somebody picks a new colour in the
    /// lobby, or the host switches to a team mode and everybody is reassigned.
    /// </summary>
    public void ApplyColour()
    {
        if (rig != null && PV != null && PV.Owner != null)
            rig.Tint(PlayerColours.For(PV.Owner));
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (targetPlayer != PV.Owner)
            return;

        if (changedProps.ContainsKey(PlayerColours.ColourKey)
            || changedProps.ContainsKey(PlayerColours.TeamKey))
            ApplyColour();

        // A new loadout means new weapon objects on every client, owner included - this is how
        // gun game moves you up a rung without respawning you.
        if (changedProps.ContainsKey(LoadoutKey))
        {
            BuildLoadout();
            return;
        }

        if (changedProps.ContainsKey(ItemIndexKey) && !PV.IsMine)
            EquipItem((int)changedProps[ItemIndexKey]);

        // Climbing a rung swaps the gun out of your hands with no warning. Without something
        // saying so it reads as the game taking your weapon away, which is roughly the opposite
        // of what just happened.
        if (PV.IsMine && Hud != null && changedProps.ContainsKey(MatchState.RungKey)
            && MatchState.Mode == MatchMode.GunGame)
        {
            int rung = MatchState.LadderRung(PV.Owner);

            // Compared against what was last announced rather than trusting the property to have
            // changed. Belt and braces with the master only writing it on a climb - a rung reset
            // between matches also lands here, and "RUNG 1" shouted at the start of every match
            // is the same noise from the other direction.
            if (rung != announcedRung)
            {
                announcedRung = rung;

                Hud.ShowRungUp(rung, WeaponLoadout.DisplayName(
                    MatchState.Rules.LoadoutForRung(rung, WeaponLoadout.GunGameLadder)[0]));
            }
        }
    }


    // verticalLookRotation is maintained on both sides - Look() sets it locally, ApplyRemoteLook
    // lerps it toward the replicated value - so the rig reads the same field either way.
    void FeedRig()
    {
        if (rig == null)
            return;

        // Pulsed white while protected, on every client including this one's own shadow. The
        // shooter has to be able to see that their shots are not landing for a reason.
        bool shielded = IsProtected;

        if (shielded || wasProtected)
        {
            Color colour = PlayerColours.For(PV.Owner);

            if (shielded)
            {
                float pulse = 0.45f + Mathf.PingPong(Time.time * 2.4f, 0.45f);
                colour = Color.Lerp(colour, Color.white, pulse);
            }

            rig.Tint(colour);
            wasProtected = shielded;
        }

        rig.LookPitch = verticalLookRotation;

        // Runs on remote copies too - they build the same loadout and receive the same equipped
        // index, so they know whether you're holding something that needs two hands.
        SingleShotGun held = ActiveGun;
        rig.TwoHandedGrip = held == null || held.Info == null || held.Info.twoHanded;
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

        // Same 100x bone scale that was inflating the hitboxes. Left alone, everyone else saw
        // you holding a banana the size of a building, positioned metres off your hand because
        // the offset below was being multiplied by a hundred too.
        Hitbox.Neutralise(itemHolder);

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
    public void ReportShot(string weaponName, Vector3 endPoint, Vector3 endNormal, bool hit)
    {
        PV.RPC(nameof(RPC_WeaponFired), RpcTarget.All, weaponName, endPoint, endNormal, hit);
    }

    /// <summary>
    /// Throws this player. Explosions and grapples both come through here.
    ///
    /// Only ever called on the client that owns the body - movement is simulated locally and a
    /// remote client pushing somebody else's character would be overwritten by the next
    /// transform update anyway.
    /// </summary>
    public void Launch(Vector3 impulse)
    {
        PlayerMovement movement = GetComponent<PlayerMovement>();

        if (movement != null)
            movement.AddImpulse(impulse);
    }

    /// <summary>
    /// A shell is in the air. Everybody simulates it; only the shooter's copy deals damage.
    ///
    /// Sent as an origin and a direction rather than as a position every frame, because the
    /// flight is deterministic - given those two numbers every client draws the same arc, and
    /// there is nothing left worth sending.
    /// </summary>
    public void ReportProjectile(string weaponName, Vector3 origin, Vector3 direction)
    {
        PV.RPC(nameof(RPC_ProjectileFired), RpcTarget.All, weaponName, origin, direction);
    }

    [PunRPC]
    void RPC_ProjectileFired(string weaponName, Vector3 origin, Vector3 direction, PhotonMessageInfo info)
    {
        GunInfo gun = Resources.Load<GunInfo>($"Guns/{weaponName}");

        if (gun == null)
            return;

        GameAudio.PlayAt($"{GameAudio.Shoot}/{weaponName}", origin, GameAudio.ShotVolume);

        if (gun.Weight >= gun.layeredAbove)
        {
            GameAudio.PlayAtDelayed($"{GameAudio.Shoot}/{weaponName}", origin,
                                    GameAudio.ShotVolume * 0.7f, 0.6f, 0.035f);
        }

        Projectile.Launch(gun, origin, direction, this, PV.IsMine);
    }

    [PunRPC]
    void RPC_WeaponFired(string weaponName, Vector3 endPoint, Vector3 endNormal, bool hit)
    {
        // The bang happens whether or not you connected. It used to be inside the hit path, so
        // missing was completely silent.
        GameAudio.PlayAt($"{GameAudio.Shoot}/{weaponName}", transform.position, GameAudio.ShotVolume);

        // A second layer, pitched down and a hair late, on anything heavy. It is what separates
        // a shotgun from a loud click: the crack says it fired, the body says how big it was.
        // Light weapons skip it - a rifle at ten rounds a second does not want two samples per
        // shot fighting each other.
        GunInfo fired = Resources.Load<GunInfo>($"Guns/{weaponName}");

        if (fired != null && fired.Weight >= fired.layeredAbove)
        {
            GameAudio.PlayAtDelayed($"{GameAudio.Shoot}/{weaponName}", transform.position,
                                    GameAudio.ShotVolume * 0.7f, 0.6f, 0.035f);

            // A third layer on the heaviest weapons, lower and later still. Two layers gave the
            // shotgun a body; three give it a room to be in, which is the part that was missing -
            // a real shotgun is mostly what happens after the bang.
            if (fired.Weight > 0.8f)
            {
                GameAudio.PlayAtDelayed($"{GameAudio.Shoot}/{weaponName}", transform.position,
                                        GameAudio.ShotVolume * 0.55f, 0.38f, 0.095f);
            }
        }

        if (hit)
            GameAudio.PlayAt(GameAudio.Impact, endPoint, GameAudio.ImpactVolume);

        // Flash, tracer and decal on the weapon that actually fired, so it's right for
        // spectators too.
        foreach (SingleShotGun gun in GetComponentsInChildren<SingleShotGun>(true))
        {
            if (gun.name == weaponName)
            {
                gun.PlayFireEffects(endPoint, endNormal, hit);
                break;
            }
        }
    }

    /// Called by a weapon on each shot. Kick is (pitch, yaw) in degrees.
    // The weapon jolts back toward you when it fires, then settles.
    //
    // Recoil moves the view, which tells you the shot went high. This tells you the weapon did
    // something - without it a gun that fires ten times a second is a static banana with a
    // noise attached, and the whole thing reads as a screenshot that occasionally beeps.
    Vector3 viewKick;
    const float kickBack = 0.055f;
    const float kickRise = 0.018f;
    const float kickRecovery = 14f;

    public void AddRecoil(Vector2 kick, float recovery, float speed)
    {
        viewKick += new Vector3(0f, kickRise, -kickBack);

        recoilTarget += kick;
        recoilRecovery = recovery;
        recoilSpeed = speed;
        lastRecoilAt = Time.time;
    }

    void Look()
    {
        // aimSensitivity is the weapon's own zoom compensation - a scope that magnifies four
        // times has to slow the mouse by the same ratio or aiming makes you twitchier, not
        // steadier. The player's ADS preference multiplies that rather than replacing it, and
        // only while actually scoped.
        float sensitivity = GameSettings.Sensitivity * aimSensitivity;

        if (IsAiming)
            sensitivity *= GameSettings.AdsSensitivity;

        float invert = GameSettings.InvertY ? -1f : 1f;

        horizontalLookRotation += Input.GetAxisRaw("Mouse X") * sensitivity;
        verticalLookRotation -= Input.GetAxisRaw("Mouse Y") * sensitivity * invert;
        verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);

        UpdateRecoil();

        // Recoil rides on top of the look angles rather than being folded into them, so
        // recovering doesn't undo where you actually pointed the mouse.
        transform.localEulerAngles = new Vector3(0f, horizontalLookRotation + recoilOffset.y, 0f);
        cameraHolder.transform.localEulerAngles = new Vector3(verticalLookRotation - recoilOffset.x, 0f, 0f);
    }

    // Aiming down the banana.
    //
    // Three things move together, and they have to: the view narrows, the weapon comes up to
    // the centre, and the mouse slows down by the same ratio the view narrowed. Without that
    // last part a scope makes you twitchier rather than steadier - the same hand movement
    // sweeps the same angle across a much smaller window.
    /// <param name="held">
    /// Whether the aim button is down. Passed in rather than read here so the behaviour can be
    /// driven without an input device - there is no mouse in a batch mode play test, and an
    /// aim mode nothing can exercise is an aim mode nobody has checked.
    /// </param>
    void UpdateAim(bool held)
    {
        // Gun game swaps your weapon out from under you by rebuilding the loadout, and Destroy
        // is deferred, so there is a window where you are holding nothing at all. Everything
        // below has to survive that.
        SingleShotGun gun = ActiveGun;
        GunInfo info = gun != null ? gun.Info : null;
        bool wants = info != null && info.canAim && held && !gun.Reloading;

        IsAiming = wants;

        if (LocalCamera == null || itemHolder == null)
            return;

        // Both punches decay on unscaled time, because hitstop is usually running when they
        // are - on scaled time they'd hang at full stretch through the freeze.
        float decay = 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime);
        fovPunch = Mathf.Lerp(fovPunch, 0f, decay);
        firePunch = Mathf.Lerp(firePunch, 0f, decay);

        float targetFov = (wants ? info.aimFov : baseFov) + fovPunch * 7f + firePunch * 2.2f;
        float t = 1f - Mathf.Exp(-aimSpeed * Time.deltaTime);

        LocalCamera.fieldOfView = Mathf.Lerp(LocalCamera.fieldOfView, targetFov, t);

        // Sensitivity follows the actual field of view rather than the target, so it stays
        // matched to what you're looking at all the way through the transition.
        aimSensitivity = LocalCamera.fieldOfView / baseFov;

        // The weapon goes away entirely, the way a scoped rifle does in Counter-Strike, and the
        // HUD draws a scope instead.
        //
        // Tried posing it instead - centred, pushed out in front, sighting along the top. It
        // never worked, and it can't: narrowing the field of view magnifies whatever is in it,
        // and the weapon is in it, so it grows exactly as fast as the world does. Engines that
        // keep the model on screen while scoped render it through a second camera at its own
        // fixed field of view. Not drawing it is the same answer for none of the work.
        if (gun != null)
            gun.SetVisible(!wants);

        // Kick decays toward nothing wherever the weapon is being held.
        viewKick = Vector3.Lerp(viewKick, Vector3.zero, 1f - Mathf.Exp(-kickRecovery * Time.deltaTime));

        // Back where it belongs, since aiming no longer moves it.
        itemHolder.localPosition = Vector3.Lerp(itemHolder.localPosition, weaponViewOffset + viewKick, t);
        itemHolder.localRotation = Quaternion.Slerp(itemHolder.localRotation,
                                                    Quaternion.Euler(weaponViewRotation), t);

        if (sway != null)
            sway.SetRest(itemHolder.localPosition, itemHolder.localRotation, wants);
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
            stream.SendNext(IsProtected);
        }
        else
        {
            remoteVerticalLook = (float)stream.ReceiveNext();
            remoteProtected = (bool)stream.ReceiveNext();
        }
    }



    void UpdateCursorLock()
    {
        // One rule: the cursor is free exactly while the settings screen is up, and captured
        // the rest of the time.
        //
        // It used to unlock on escape and only re-lock when you clicked, which meant closing
        // settings left you with a loose cursor and a game that didn't respond until you
        // clicked somewhere - and that click also fired your gun. Tying it to the one thing
        // that actually wants a cursor means there is no state to get stuck in.
        cursorLocked = !SettingsMenu.IsOpen;
        CursorCaptured = cursorLocked;

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

        // Just landed. Being shot before you have finished arriving is the least interesting
        // way to lose a fight, and no spawn placement can rule it out entirely.
        if (IsProtected)
            return;

        float shieldBefore = Overshield;
        currentHealth = Absorb(currentHealth, damage);

        // The shield going is worth its own sound. It is the one thing you cannot read off the
        // bar in time - by the moment you have looked down to check, the next shot has landed.
        if (shieldBefore > 0f && Overshield <= 0f && currentHealth > 0f)
            ShieldBreak();

        // 2D - this happened to you, not near you.
        GameAudio.Play2D(GameAudio.Hurt, GameAudio.HurtVolume, 0.1f);

        // Which way it came from. The shooter's body is looked up rather than sent, because the
        // position at the moment the shot landed is what matters and they have moved since the
        // RPC left them.
        if (Hud != null && info.Sender != null)
        {
            foreach (PlayerController other in
                     FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            {
                if (other != null && other.View != null && other.View.Owner == info.Sender)
                {
                    Hud.ShowDamageFrom(other.transform.position);
                    break;
                }
            }
        }

        // Shake without a stop. Being shot shouldn't freeze your game - that's the one moment
        // you most need control, and stealing it turns a fight into a slideshow.
        Juice.Shake(Mathf.Clamp01(damage / 50f));

        if (currentHealth <= 0f)
            Die(info.Sender, weapon, headshot);
    }

    /// <summary>
    /// Puts health back, never above the maximum. Local only - health isn't replicated, each
    /// client owns its own and tells others when it runs out.
    /// </summary>
    public void Heal(float amount)
    {
        if (dead || amount <= 0f)
            return;

        float before = currentHealth;

        // Past the normal maximum it becomes overshield, up to the ceiling. Without this a
        // heal is worth nothing to the one person who earned it - whoever is on a streak is
        // usually the person already at full health.
        float ceiling = Killstreak > 0 ? overshieldCeiling : maxHealth;
        currentHealth = Mathf.Min(ceiling, currentHealth + amount);

        if (currentHealth > before && Hud != null)
            Hud.ShowHeal(currentHealth - before);
    }

    /// Anything above the normal maximum, for the HUD to draw differently.
    /// <summary>
    /// How much damage a point of overshield soaks up. Two, so shield points are worth double
    /// ordinary health.
    ///
    /// Without this the overshield was decorative against the one weapon it most needed to
    /// matter against. The shotgun does 108 a pull: at 140 health that kills in two, and at a
    /// full 200 it still kills in two, so sixty points of reward for a killstreak bought
    /// exactly nothing. Now the same sixty points absorb a hundred and twenty damage, which
    /// turns that into three - and three is the difference between being ambushed and getting
    /// to shoot back.
    /// </summary>
    public const float ShieldToughness = 2f;

    /// <summary>
    /// Damage against a pool that may have overshield sitting on top of it.
    ///
    /// Static and pure so it can be checked without a player, a room or a game running - this
    /// is a rule about how the game plays rather than plumbing, and it's the sort of thing that
    /// is easy to get subtly wrong and never notice.
    /// </summary>
    public static float Absorb(float health, float damage, float max = maxHealth)
    {
        float shield = Mathf.Max(0f, health - max);

        if (shield <= 0f || damage <= 0f)
            return health - damage;

        // The shield can eat twice its own value before it's gone, and whatever is left over
        // carries on into ordinary health at full strength.
        float eaten = Mathf.Min(shield * ShieldToughness, damage);

        return health - eaten / ShieldToughness - (damage - eaten);
    }

    /// <summary>
    /// Plays the shield shattering, falling back to something breakage-shaped if no clip has
    /// been dropped in yet.
    ///
    /// The fallback is a real recorded impact pitched up rather than anything synthesised -
    /// pitching a sourced clip is arranging, and the alternative has been tried and rejected
    /// four times.
    /// </summary>
    void ShieldBreak()
    {
        if (Resources.LoadAll<AudioClip>("Audio/" + GameAudio.Shield).Length > 0)
        {
            GameAudio.Play2D(GameAudio.Shield, GameAudio.ShieldVolume, 0.05f);
            return;
        }

        GameAudio.PlayPitched(GameAudio.Impact, null, GameAudio.ShieldVolume, 1.9f);
    }

    [Header("Spawning")]
    [Tooltip("Seconds of immunity after coming back. Ends early the moment you shoot.")]
    [SerializeField] float spawnProtectionSeconds = 2f;

    float spawnedAt = -999f;
    bool gaveUpProtection;
    bool remoteProtected;
    bool wasProtected;

    /// <summary>
    /// Whether this player cannot currently be hurt.
    ///
    /// Time based and replicated as a single bool on the existing stream rather than as a
    /// property, because it changes twice per life and a custom property write is a server round
    /// trip - and because the visible flash has to agree with the immunity on every screen. A
    /// shooter emptying a magazine into someone who is not taking damage needs to be able to see
    /// why, or it reads as the hit registration being broken.
    /// </summary>
    public bool IsProtected
    {
        get
        {
            if (PV == null || !PV.IsMine)
                return remoteProtected;

            return !gaveUpProtection && !dead
                   && Time.time < spawnedAt + spawnProtectionSeconds
                   && MatchState.Phase != MatchPhase.Over;
        }
    }

    /// <summary>
    /// Gives up the shield. Called the moment you fire.
    ///
    /// Spawn protection exists so you can land and get your bearings, not so you can walk into a
    /// fight invulnerable. Shooting is the clearest possible signal that you have stopped
    /// getting your bearings.
    /// </summary>
    public void DropProtection() => gaveUpProtection = true;

    public float Overshield => Mathf.Max(0f, currentHealth - maxHealth);
    public float MaxHealth => maxHealth;

    /// The top of the bar, so the HUD can scale to it rather than guessing.
    public float OvershieldCeiling => overshieldCeiling;

    /// Called on the killer's own client when one of their shots finishes someone off.
    void RewardKill()
    {
        Killstreak++;

        multikill = Time.time < multikillLapsesAt ? multikill + 1 : 1;
        multikillLapsesAt = Time.time + multikillWindow;

        float heal = healPerKill + Mathf.Min(maxStreakHeal, (Killstreak - 1) * healPerStreak);
        Heal(heal);

        if (Hud != null)
            Hud.ShowKill(multikill, Killstreak);

        // The kill sound climbs with the multikill, same idea as the hit combo. Two kills in
        // four seconds should not sound identical to two kills a minute apart.
        GameAudio.PlayPitched(GameAudio.Kill, "kill", GameAudio.KillVolume,
                              1f + Mathf.Min(multikill - 1, 4) * 0.09f);

        // A short punch of field of view. Small enough not to disorient, big enough to feel.
        fovPunch = 1f;
    }

    // A quick widening of the view on a kill, and a smaller one every time you fire. Reads as
    // the world lurching rather than the camera moving, which is why it's separate from shake.
    float fovPunch;
    float firePunch;

    public void AddFirePunch(float strength) => firePunch = Mathf.Max(firePunch, strength);

    /// killer is null for falling off the map and anything else self inflicted.
    void Die(Player killer, string weapon, bool headshot)
    {
        if (dead)
            return;

        dead = true;
        Killstreak = 0;

        // Only the victim runs this - it is the one client that knows the health hit zero - so
        // it has to tell everyone else. Before this nobody but you knew you had died, which
        // left the kill feed and the match itself with nothing to listen to.
        int killerActor = killer != null ? killer.ActorNumber : -1;

        // Which kill feed line everyone reads, rolled once by the person it happened to and
        // sent with the death. Rolling it on arrival instead would give every client a
        // different sentence for the same kill, which people notice immediately when they're
        // all shouting about it.
        byte flavour = (byte)Random.Range(0, 256);

        PV.RPC(nameof(RPC_Died), RpcTarget.All, killerActor, weapon ?? string.Empty, headshot, flavour);

        if (RoomManager.Instance != null)
            RoomManager.Instance.HandleLocalDeath(transform.position, transform.forward, killer);
    }

    [PunRPC]
    void RPC_Died(int killerActor, string weapon, bool headshot, byte flavour)
    {
        GameAudio.PlayAt(GameAudio.Death, transform.position, GameAudio.DeathVolume);

        Player killer = killerActor >= 0 && PhotonNetwork.InRoom
            ? PhotonNetwork.CurrentRoom.GetPlayer(killerActor)
            : null;

        // Everyone gets the kill feed entry; only the master acts on the score, so two clients
        // can never disagree about it and a late joiner reads the same numbers as everyone else.
        // You did that. 2D, and only for the person who gets to feel good about it - a kill
        // sound that plays for everyone is just another explosion.
        if (killer != null && killer.IsLocal && killer != PV.Owner)
        {
            // Healing happens here rather than in MatchState because it's a local, felt thing
            // rather than a scored one - nobody else needs to know your health went up.
            if (Local != null)
                Local.RewardKill();

            // The biggest stop in the game. Killing someone is the thing every other piece of
            // feedback has been building toward, so it gets the whole budget.
            Juice.Hit(1f);
        }

        MatchState.ReportKill(killer, PV.Owner, weapon, headshot, flavour);
    }
}
