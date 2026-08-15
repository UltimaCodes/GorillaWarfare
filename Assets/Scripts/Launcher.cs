using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;
using TMPro;
using Photon.Realtime;
using Hashtable = ExitGames.Client.Photon.Hashtable;

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


    // Used to be static and never cleared, so dead rooms hung around in the browser for
    // the whole session (and across play sessions in the editor).
    readonly Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();

    // Tracking the rows we spawned rather than walking the container's children - Destroy
    // is deferred, so rebuilding twice in one frame gave us duplicates.
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

        // PUN defaults to 10 serializations/sec which looks choppy. Has to stay <= SendRate.
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
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        cachedRoomList.Clear();
        ClearList(roomListItems);
        OpenMenu("title");
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

        // Used to be silent - you'd just sit on whatever screen you were on.
        ShowError($"Disconnected: {cause}");
    }

    public void CreateRoom()
    {
        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);

        string roomName = roomNameInputField != null ? roomNameInputField.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(roomName))
        {
            ShowError("Enter a room name.");
            return;
        }

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayersPerRoom,
            IsVisible = true,
            IsOpen = true,

            // A room opens on deathmatch and the host changes it from the lobby, where they can
            // see who turned up. It has to be a room property so late joiners get it from the
            // server rather than from whoever answers first, and it has to be declared for the
            // lobby so the browser can show what a room is running before you commit to it -
            // including after the host changes their mind.
            CustomRoomProperties = new Hashtable { { MatchState.ModeKey, (int)MatchMode.Deathmatch } },
            CustomRoomPropertiesForLobby = new[] { MatchState.ModeKey },
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

    // Wasn't handled at all - clicking a full room left you stuck on the loading screen.
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        ShowError($"Could not join room: {message}");
    }

    public void StartGame()
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        GameAudio.Play2D(GameAudio.UI, "confirm", GameAudio.UiVolume);

        PhotonNetwork.LoadLevel(1);
    }

    public void LeaveRoom()
    {
        GameAudio.Play2D(GameAudio.UI, "back", GameAudio.UiVolume);
        PhotonNetwork.LeaveRoom();
        OpenMenu("loading");
    }

    public void JoinRoom(RoomInfo info)
    {
        if (info == null)
            return;

        GameAudio.Play2D(GameAudio.UI, "click_001", GameAudio.UiVolume);
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

            // Photon still reports empty rooms for a moment before they're torn down.
            if (info.RemovedFromList || info.PlayerCount == 0)
                cachedRoomList.Remove(info.Name);
            else
                cachedRoomList[info.Name] = info;
        }

        ClearList(roomListItems);

        if (roomListItemPrefab == null || roomListContent == null)
            return;

        foreach (KeyValuePair<string, RoomInfo> entry in cachedRoomList)
        {
            GameObject row = Instantiate(roomListItemPrefab, roomListContent);
            row.GetComponent<RoomListItem>().SetUp(entry.Value);
            roomListItems.Add(row);
        }
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        AddPlayerListItem(newPlayer);
    }

    // There was no handler for this at all, so anyone who left the lobby stayed in the list
    // until something else rebuilt it - which, sitting in a room, nothing does.
    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        ClearList(playerListItems);

        Player[] players = PhotonNetwork.PlayerList;
        for (int i = 0; i < players.Length; i++)
            AddPlayerListItem(players[i]);
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
        GameAudio.Play2D(GameAudio.UI, "error_001", GameAudio.UiVolume);

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
