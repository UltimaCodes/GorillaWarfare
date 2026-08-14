using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;
using Photon.Realtime;
using UnityEngine.UI;

public class PlayerController : MonoBehaviourPunCallbacks, IDamageable, IPunObservable
{
    [SerializeField] float mouseSensitivity, sprintSpeed, walkSpeed, jumpForce, smoothTime;
    [SerializeField] GameObject cameraHolder;
    [SerializeField] Item[] items;
    [SerializeField] Image healthbarImage;
    [SerializeField] GameObject ui;
    int itemIndex;
    int previousItemIndex = -1;
    bool grounded;
    Vector3 moveAmount;
    Vector3 smoothMoveVelocity;
    float verticalLookRotation;
    float horizontalLookRotation;
    bool cursorLocked = true;
    const float maxHealth = 100f;
    float currentHealth = maxHealth;

    // Pitch is applied to cameraHolder, which is not covered by any PhotonTransformView.
    // We send it ourselves as a single float so remote players can see where we're aiming.
    float remoteVerticalLook;
    const float pitchLerpSpeed = 15f;

    // The player camera is Untagged, so Camera.main returns null and anything needing the local
    // view (nameplate billboards) had to guess with FindObjectOfType -- which can return another
    // player's camera in the window before Start destroys it. Publishing it here is unambiguous.
    public static Camera LocalCamera { get; private set; }

    PlayerManager playerManager;

    Rigidbody rb;
    PhotonView PV;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        PV = GetComponent<PhotonView>();

        ResolvePlayerManager();
    }

    // Runs on every client, including ones that may not have registered the owner's
    // PlayerManager view yet. Previously this chained straight off PhotonView.Find and threw
    // a NullReferenceException on a late join, aborting the rest of Awake.
    bool ResolvePlayerManager()
    {
        if (playerManager != null)
            return true;

        object[] data = PV.InstantiationData;
        if (data == null || data.Length == 0)
        {
            Debug.LogError($"[Spawn] view={PV.ViewID} has no InstantiationData; cannot resolve PlayerManager.", this);
            return false;
        }

        int managerViewID = (int)data[0];
        PhotonView managerView = PhotonView.Find(managerViewID);
        if (managerView == null)
        {
            Debug.LogWarning($"[Spawn] view={PV.ViewID} could not find PlayerManager view {managerViewID}. Will retry on demand.", this);
            return false;
        }

        playerManager = managerView.GetComponent<PlayerManager>();
        return playerManager != null;
    }

    void Start()
    {
        LogSpawn("start");

        if (PV.IsMine)
        {
            LocalCamera = GetComponentInChildren<Camera>();
            EquipItem(0);
        }
        else
        {
            Camera ownCamera = GetComponentInChildren<Camera>();
            if (ownCamera != null)
                Destroy(ownCamera.gameObject);

            if (rb != null)
                Destroy(rb);

            if (ui != null)
                Destroy(ui);

            // The invisibility report is "others can't see me". Re-check a remote player a
            // couple of seconds in, once replication has had time to deliver something, so we
            // can tell "never spawned" from "spawned in the wrong place" from "spawned fine".
            StartCoroutine(LogSpawnAfterSettle());
        }
    }

    void OnDestroy()
    {
        // Respawning destroys and recreates the controller, so a stale static would otherwise
        // point at a destroyed camera until the replacement's Start runs.
        if (LocalCamera != null && PV != null && PV.IsMine)
            LocalCamera = null;
    }

    IEnumerator LogSpawnAfterSettle()
    {
        yield return new WaitForSeconds(2f);
        LogSpawn("settled");
    }

    // Diagnostic for the late-join visibility bug. Reports where the object actually is and
    // whether anything is drawing, rather than assuming the failure is a rendering one.
    void LogSpawn(string phase)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        int drawing = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i].enabled && renderers[i].gameObject.activeInHierarchy)
                drawing++;
        }

        Debug.Log(
            $"[Spawn:{phase}] view={PV.ViewID} mine={PV.IsMine} owner={PV.Owner?.NickName ?? "<none>"} " +
            $"pos={transform.position} active={gameObject.activeInHierarchy} " +
            $"renderers={drawing}/{renderers.Length} manager={(playerManager == null ? "NULL" : "ok")} " +
            $"players={PhotonNetwork.CurrentRoom?.PlayerCount} master={PhotonNetwork.IsMasterClient}", this);
    }

    void Update()
    {
        if (!PV.IsMine)
        {
            ApplyRemoteLook();
            return;
        }

        Look();
        Move();
        Jump();
        UpdateCursorLock();

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

        if(transform.position.y < -10f)
        {
            Die();
        }
    }

    void Jump()
    {
        if (Input.GetKeyDown(KeyCode.Space) && grounded)
        {
            rb.AddForce(transform.up * jumpForce);
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

    void Move()
    {
        Vector3 moveDir = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical")).normalized;

        moveAmount = Vector3.SmoothDamp(moveAmount, moveDir * (Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed), ref smoothMoveVelocity, smoothTime);
    }

    void Look()
    {
        horizontalLookRotation += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        transform.localEulerAngles = new Vector3(0f, horizontalLookRotation, 0f);

        verticalLookRotation -= Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
        verticalLookRotation = Mathf.Clamp(verticalLookRotation, -90f, 90f);
        cameraHolder.transform.localEulerAngles = new Vector3(verticalLookRotation, 0f, 0f);
    }

    // Remote players: drive cameraHolder from the replicated pitch. Lerped rather than snapped
    // because serialization only fires 10-30x/second, so raw values would visibly step.
    // cameraHolder survives on remote clients -- Start() destroys the Camera child, not its parent.
    void ApplyRemoteLook()
    {
        if (cameraHolder == null)
            return;

        verticalLookRotation = Mathf.LerpAngle(verticalLookRotation, remoteVerticalLook, Time.deltaTime * pitchLerpSpeed);
        cameraHolder.transform.localEulerAngles = new Vector3(verticalLookRotation, 0f, 0f);
    }

    // Observed by the root PhotonView alongside PhotonTransformView. Sends one float rather
    // than adding a fourth PhotonView to cameraHolder purely to carry vertical aim.
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

    public void SetGroundedState(bool _grounded)
    {
        grounded = _grounded;
    }

    void FixedUpdate()
    {
        if (!PV.IsMine)
            return;
        rb.MovePosition(rb.position + transform.TransformDirection(moveAmount) * Time.fixedDeltaTime);
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

        if (currentHealth <= 0)
        {
            Die();

            // Another unguarded cross-client lookup: the killer's PlayerManager may not be
            // resolvable here, and losing a kill credit shouldn't take the death with it.
            PlayerManager killer = PlayerManager.Find(info.Sender);
            if (killer != null)
                killer.GetKill();
            else
                Debug.LogWarning($"[Kill] no PlayerManager for sender {info.Sender?.NickName ?? "<none>"}; kill not credited.", this);
        }
    }

    void Die()
    {
        // Retry the lookup here: if Awake lost the race, the view is almost certainly
        // registered by now, and dying with a null manager would strand the player dead.
        if (!ResolvePlayerManager())
        {
            Debug.LogError($"[Spawn] view={PV.ViewID} died with no PlayerManager; cannot respawn.", this);
            return;
        }

        playerManager.Die();
    }
}
