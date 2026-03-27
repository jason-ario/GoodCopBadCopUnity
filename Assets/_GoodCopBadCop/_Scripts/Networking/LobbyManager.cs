using System;
using System.Linq;
using System.Threading.Tasks;
using Netcode.Transports.Facepunch;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode.Transports.UTP;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    public Lobby CurrentLobby { get; private set; }

    public event Action OnLobbyUpdated;
    public event Action OnKicked;

    private FacepunchTransport facepunchTransport;
    private bool startingClientFromLobbyFlow;
    private bool inviteOverlayWasOpenedByUs;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("LobbyManager: NetworkManager.Singleton is null.");
            return;
        }

        facepunchTransport = NetworkManager.Singleton.GetComponent<FacepunchTransport>();

        NetworkTransport networkTransport = NetworkManager.Singleton.GetComponent<NetworkTransport>();

        if (networkTransport is FacepunchTransport)
        {
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
            SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
            SteamFriends.OnGameOverlayActivated += OnOverlayToggled;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;
        SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        SteamFriends.OnGameOverlayActivated -= OnOverlayToggled;
    }

    private void OnOverlayToggled(bool isActive)
    {
        Debug.Log($"Steam overlay active: {isActive}");

        // Only react if we previously opened the invite overlay ourselves.
        if (!isActive && inviteOverlayWasOpenedByUs)
        {
            inviteOverlayWasOpenedByUs = false;
            CloseInviteFriendsPopUp();
        }
    }

    // =========================
    // HOST
    // =========================
    public async void CreateLobby()
    {
        if (NetworkManager.Singleton == null)
            return;

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport facepunch)
        {
            var createdLobby = await SteamMatchmaking.CreateLobbyAsync(2);

            if (!createdLobby.HasValue)
            {
                Debug.LogError("Failed to create Steam lobby.");
                return;
            }

            CurrentLobby = createdLobby.Value;
            CurrentLobby.SetPublic();
            CurrentLobby.SetJoinable(true);
            CurrentLobby.SetData("host", SteamClient.Name);

            // Host targets self for Facepunch transport.
            facepunch.targetSteamId = SteamClient.SteamId;

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                if (!NetworkManager.Singleton.StartHost())
                {
                    Debug.LogError("Failed to start NGO host.");
                    CurrentLobby.Leave();
                    CurrentLobby = default;
                    return;
                }
            }

            OnLobbyUpdated?.Invoke();
            Debug.Log($"Created lobby: {CurrentLobby.Id}");
        }
        else if (transport is UnityTransport)
        {
            Debug.Log("Starting LAN Host via UnityTransport");

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.StartHost();
            }
        }
    }

    // =========================
    // CLIENT
    // =========================
    public async void JoinLobby(ulong lobbyId)
    {
        if (NetworkManager.Singleton == null)
            return;

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport)
        {
            var lobby = new Lobby(lobbyId);
            RoomEnter joinResult = await lobby.Join();

            if (joinResult != RoomEnter.Success)
            {
                Debug.LogError($"Failed to join lobby {lobbyId}. Result: {joinResult}");
                return;
            }

            // Do not StartClient() here.
            // Let OnLobbyEntered handle that so it only happens once.
            CurrentLobby = lobby;

            if (facepunchTransport != null)
            {
                facepunchTransport.targetSteamId = lobby.Owner.Id;
            }

            Debug.Log($"Requested join for lobby {lobbyId}");
        }
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData("127.0.0.1", 7777);

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                NetworkManager.Singleton.StartClient();
            }
        }
    }

    // =========================
    // INVITES
    // =========================
    public void OpenInviteFriendsPopup()
    {
        if (CurrentLobby.Id == 0)
        {
            Debug.LogWarning("Cannot open invite popup: no active Steam lobby.");
            return;
        }

        inviteOverlayWasOpenedByUs = true;
        SteamFriends.OpenGameInviteOverlay(CurrentLobby.Id);
        Debug.Log($"Opened Steam invite popup for lobby {CurrentLobby.Id}");
    }

    public void CloseInviteFriendsPopUp()
    {
        if (UIController.Instance != null)
        {
            UIController.Instance.CloseInviteFriendsScreen();
        }
    }

    private void OnGameLobbyJoinRequested(Lobby lobby, SteamId friendId)
    {
        Debug.Log($"Steam invite accepted from {friendId} for lobby {lobby.Id}");
        JoinLobby(lobby.Id);
    }

    // =========================
    // STEAM CALLBACKS
    // =========================
    private async void OnLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;

        await Task.Delay(50);

        Debug.Log($"[OnLobbyEntered] Local members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();

        if (!NetworkManager.Singleton.IsHost &&
            !NetworkManager.Singleton.IsClient &&
            !startingClientFromLobbyFlow)
        {
            startingClientFromLobbyFlow = true;

            if (facepunchTransport != null)
            {
                facepunchTransport.targetSteamId = lobby.Owner.Id;
            }

            bool started = NetworkManager.Singleton.StartClient();
            Debug.Log($"StartClient from OnLobbyEntered: {started}");

            startingClientFromLobbyFlow = false;
        }
    }

    private async void OnLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50);

        Debug.Log($"[OnLobbyMemberJoined] {friend.Name}");
        Debug.Log($"Members now: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();
    }

    private async void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50);

        Debug.Log($"[OnLobbyMemberLeave] {friend.Name}");

        OnLobbyUpdated?.Invoke();
    }

    // =========================
    // NETCODE (SUPPLEMENTAL)
    // =========================
    private async void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        await Task.Delay(50);

        if (CurrentLobby.Id != 0)
        {
            Debug.Log($"[Host] Netcode client connected. Steam members: {CurrentLobby.Members.Count()}");
        }
        else
        {
            Debug.Log("[Host] Netcode client connected.");
        }

        OnLobbyUpdated?.Invoke();
    }

    // =========================
    // HELPERS
    // =========================
    public Friend[] GetMembersSnapshot()
    {
        if (CurrentLobby.Id == 0)
            return Array.Empty<Friend>();

        return CurrentLobby.Members.ToArray();
    }

    public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    public void ExitLobby()
    {
        inviteOverlayWasOpenedByUs = false;

        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (CurrentLobby.Id != 0)
        {
            CurrentLobby.Leave();
            CurrentLobby = default;
        }

        OnLobbyUpdated?.Invoke();
    }
}