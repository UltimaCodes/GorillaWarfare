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
    [SerializeField] Item[] items;
    [SerializeField] Image healthbarImage;
    [SerializeField] GameObject ui;
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
    bool cursorLocked = true;
    const float maxHealth = 100f;
    float currentHealth = maxHealth;

    // Pitch lives on cameraHolder, which nothing replicates - we send it ourselves.
    float remoteVerticalLook;
    const float pitchLerpSpeed = 15f;

    // Camera on the prefab is Untagged so Camera.main is useless. Billboards need this.
    public static Camera LocalCamera { get; private set; }


    PhotonView PV;
    MonkeyRig rig;

    public PhotonView View => PV;

    void Awake()
    {
        PV = GetComponent<PhotonView>();
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

        if (PV.IsMine)
        {
            LocalCamera = GetComponentInChildren<Camera>();
            gameObject.AddComponent<PlayerMovement>();

            // Sway goes on the item holder rather than the camera, so it moves the weapon
            // without moving where you're aiming.
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "ItemHolder")
                {
                    t.gameObject.AddComponent<WeaponSway>();
                    break;
                }
            }
            EquipItem(0);
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

        }
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

        Look();
        UpdateCursorLock();
        FeedRig();

        for (int i = 0; i < items.Length; i++)
        {
            if(Input.GetKeyDown((i + 1).ToString()))
            {
                EquipItem(i);
                break;
            }
        }

        if(Input.GetAxisRaw("Mouse ScrollWheel") > 0f)
        {
            if (itemIndex >= items.Length - 1)
            {
                EquipItem(0);
            }
            else
            {
                EquipItem(itemIndex + 1);
            }
        }
        else if(Input.GetAxisRaw("Mouse ScrollWheel") < 0f)
        {
            if(itemIndex <= 0)
            {
                EquipItem(items.Length - 1);
            }
            else
            {
                EquipItem(itemIndex - 1);
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            items[itemIndex].Use();
        }
        else if (Input.GetMouseButton(0) && items[itemIndex] is SingleShotGun heldGun)
        {
            heldGun.UseHeld();
        }

        if (Input.GetKeyDown(KeyCode.R) && items[itemIndex] is SingleShotGun reloadGun)
        {
            reloadGun.Reload();
        }

        if(transform.position.y < -10f)
        {
            Die();
        }
    }


    void EquipItem(int _index)
    {
        if(_index == previousItemIndex)
        {
            return;
        }

        itemIndex = _index;

        items[itemIndex].itemGameObject.SetActive(true);

        if(previousItemIndex != -1)
        {
            items[previousItemIndex].itemGameObject.SetActive(false);
        }

        previousItemIndex = itemIndex;

        if (PV.IsMine)
        {
            Hashtable hash = new Hashtable();
            hash.Add("itemIndex", itemIndex);
            PhotonNetwork.LocalPlayer.SetCustomProperties(hash);
        }
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey("itemIndex") && !PV.IsMine && targetPlayer == PV.Owner)
        {
            EquipItem((int)changedProps["itemIndex"]);
        }
    }


    // verticalLookRotation is maintained on both sides - Look() sets it locally, ApplyRemoteLook
    // lerps it toward the replicated value - so the rig reads the same field either way.
    void FeedRig()
    {
        if (rig != null)
            rig.LookPitch = verticalLookRotation;
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
        // Decay the target so the kick doesn't stack forever, then chase it. Two stages is what
        // makes it feel like a spring instead of a teleport - the shot snaps the view and it
        // settles back smoothly.
        recoilTarget = Vector2.Lerp(recoilTarget, Vector2.zero, recoilRecovery * recoilSpeed * Time.deltaTime);
        recoilOffset = Vector2.Lerp(recoilOffset, recoilTarget, 1f - Mathf.Exp(-recoilSpeed * 2f * Time.deltaTime));
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

    public void TakeDamage(float damage)
    {
        PV.RPC(nameof(RPC_TakeDamage), PV.Owner, damage);
    }

    [PunRPC]
    void RPC_TakeDamage(float damage, PhotonMessageInfo info)
    {
        currentHealth -= damage;

        healthbarImage.fillAmount = currentHealth / maxHealth;

        // 2D - this happened to you, not near you.
        GameAudio.Play2D(GameAudio.Hurt, 0.7f, 0.1f);

        if (currentHealth <= 0)
        {
            // Credit first - Die() destroys this object on the way out.
            RoomManager.CreditKill(info.Sender);
            Die();
        }
    }

    void Die()
    {
        GameAudio.PlayAt(GameAudio.Death, transform.position, 0.8f);

        RoomManager.CreditDeath(PhotonNetwork.LocalPlayer);

        if (RoomManager.Instance != null)
            RoomManager.Instance.RespawnLocalPlayer();
    }
}
