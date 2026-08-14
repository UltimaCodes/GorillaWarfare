using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using TMPro;
using Photon.Realtime;

public class Launcher : MonoBehaviourPunCallbacks
{
    public static Launcher Instance;

    [SerializeField] TMP_InputField roomNameInputField;
    [SerializeField] TMP_Text errorText;
    [SerializeField] TMP_Text roomNameText;
    [SerializeField] Transform roomListContent;
    [SerializeField] GameObject roomListItemPrefab;
    [SerializeField] GameObject playerListItemPrefab;
    [SerializeField] Transform playerListContent;
    [SerializeField] GameObject startGameButton;

    [SerializeField] byte maxPlayersPerRoom = 8;

    // Was static, and never cleared. Rooms that had closed stayed in the dictionary for the
    // lifetime of the process, so the browser listed dead rooms that failed on click -- and in
    // the editor they survived across play sessions entirely.
    readonly Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();

    // Tracking spawned rows instead of walking the container's children: Destroy is deferred to
    // end of frame, so a rebuild that ran twice in one frame previously produced duplicate rows.
    readonly List<GameObject> roomListItems = new List<GameObject>();
    readonly List<GameObject> playerListItems = new List<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        // PUN serializes 10x/second by default while sending 30x/second, so remote positions and
        // aim arrive ten times a second and are interpolated between. SerializationRate must stay
        // at or below SendRate, since serialized updates are queued and flushed on the next send.
        PhotonNetwork.SendRate = 30;
        PhotonNetwork.SerializationRate = 20;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        Debug.Log("[Launcher] connecting to server");
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[Launcher] connected to master");
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        cachedRoomList.Clear();
        ClearList(roomListItems);
        OpenMenu("title");
        Debug.Log("[Launcher] lobby joined");
    }

    public override void OnLeftLobby()
    {
        cachedRoomList.Clear();
        ClearList(roomListItems);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        cachedRoomList.Clear();
        ClearList(roomListItems);
        ClearList(playerListItems);

        // Previously silent: a dropped connection left whichever menu was open on screen with no
        // indication anything had gone wrong.
        ShowError($"Disconnected: {cause}");
    }

    public void CreateRoom()
    {
        string roomName = roomNameInputField != null ? roomNameInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(roomName))
        {
            ShowError("Enter a room name.");
            return;
        }

        // No RoomOptions were passed before, so rooms had no player cap at all.
        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true
        };

        PhotonNetwork.CreateRoom(roomName, options);
        OpenMenu("loading");
    }

    public override void OnJoinedRoom()
    {
        OpenMenu("room");

        if (roomNameText != null)
            roomNameText.text = PhotonNetwork.CurrentRoom.Name;

        ClearList(playerListItems);

        Player[] players = PhotonNetwork.PlayerList;
        for (int i = 0; i < players.Length; i++)
            AddPlayerListItem(players[i]);

        if (startGameButton != null)
            startGameButton.SetActive(PhotonNetwork.IsMasterClient);
    }

    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        if (startGameButton != null)
            startGameButton.SetActive(PhotonNetwork.IsMasterClient);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        ShowError($"Room creation failed: {message}");
    }

    // Was missing entirely. Clicking a room that had filled up or closed left the player sitting
    // on the loading screen forever with no error and no way back.
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        ShowError($"Could not join room: {message}");
    }

    public void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        PhotonNetwork.LoadLevel(1);
    }

    public void LeaveRoom()
    {
        PhotonNetwork.LeaveRoom();
        OpenMenu("loading");
    }

    public void JoinRoom(RoomInfo info)
    {
        if (info == null)
            return;

        PhotonNetwork.JoinRoom(info.Name);
        OpenMenu("loading");
    }

    public override void OnLeftRoom()
    {
        ClearList(playerListItems);
        OpenMenu("title");
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        for (int i = 0; i < roomList.Count; i++)
        {
            RoomInfo info = roomList[i];

            // Also drop empty rooms: Photon reports them before they are fully torn down, and
            // they were previously kept and rendered as joinable.
            if (info.RemovedFromList || info.PlayerCount == 0)
                cachedRoomList.Remove(info.Name);
            else
                cachedRoomList[info.Name] = info;
        }

        ClearList(roomListItems);

        foreach (KeyValuePair<string, RoomInfo> entry in cachedRoomList)
        {
            if (roomListItemPrefab == null || roomListContent == null)
                break;

            GameObject row = Instantiate(roomListItemPrefab, roomListContent);
            row.GetComponent<RoomListItem>().SetUp(entry.Value);
            roomListItems.Add(row);
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        AddPlayerListItem(newPlayer);
    }

    void AddPlayerListItem(Player player)
    {
        if (playerListItemPrefab == null || playerListContent == null || player == null)
            return;

        GameObject row = Instantiate(playerListItemPrefab, playerListContent);
        row.GetComponent<PlayerListItem>().SetUp(player);
        playerListItems.Add(row);
    }

    void ClearList(List<GameObject> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
                Destroy(items[i]);
        }

        items.Clear();
    }

    void ShowError(string message)
    {
        Debug.LogWarning($"[Launcher] {message}");

        if (errorText != null)
            errorText.text = message;

        OpenMenu("error");
    }

    void OpenMenu(string menuName)
    {
        if (MenuManager.Instance != null)
            MenuManager.Instance.OpenMenu(menuName);
    }

    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
