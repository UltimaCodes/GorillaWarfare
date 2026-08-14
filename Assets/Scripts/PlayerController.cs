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

    // Pitch lives on cameraHolder, which nothing replicates - we send it ourselves.
    float remoteVerticalLook;
    const float pitchLerpSpeed = 15f;

    // Camera on the prefab is Untagged so Camera.main is useless. Billboards need this.
    public static Camera LocalCamera { get; private set; }


    Rigidbody rb;
    PhotonView PV;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        PV = GetComponent<PhotonView>();
    }

    void Start()
    {
        LogSpawn("start");

        // Added here rather than on the prefab so there's nothing to wire up. Runs on remote
        // players too, since their transforms are replicated - so you hear their steps.
        gameObject.AddComponent<FootstepPlayer>();

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

            // Check again once replication has had a moment, so we can tell "never spawned"
            // from "spawned somewhere silly".
            StartCoroutine(LogSpawnAfterSettle());
        }
    }

    void OnDestroy()
    {
        // Respawning recreates the controller, so don't leave a dead camera in the static.
        if (LocalCamera != null && PV != null && PV.IsMine)
            LocalCamera = null;
    }

    IEnumerator LogSpawnAfterSettle()
    {
        yield return new WaitForSeconds(2f);
        LogSpawn("settled");
    }

    // TODO: rip this out once the late-join visibility bug is closed.
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
            $"renderers={drawing}/{renderers.Length} " +
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
