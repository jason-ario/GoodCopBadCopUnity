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
    public event Action<string> OnJoinFailed;

    private FacepunchTransport facepunchTransport;
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

        NetworkTransport networkTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

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

    private void Update()
    {
        SteamClient.RunCallbacks();
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

    private const int MaxLobbyMembers = 2;

    // =========================
    // HOST
    // =========================

    /// <summary>
    /// Creates a lobby and starts the NGO host. Returns true on success, false on failure.
    /// Awaiting this ensures NetworkManager is fully started before any RPC calls are made.
    /// </summary>
    public async Task<bool> CreateLobby()
    {
        if (NetworkManager.Singleton == null)
            return false;

        // Clean up any previous lobby before creating a new one.
        if (CurrentLobby.Id != 0)
        {
            CurrentLobby.Leave();
            CurrentLobby = default;
        }

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport facepunch)
        {
            Debug.Log("[CreateLobby] FacepunchTransport detected — creating Steam lobby...");
            var createdLobby = await SteamMatchmaking.CreateLobbyAsync(MaxLobbyMembers);

            if (!createdLobby.HasValue)
            {
                Debug.LogError("[CreateLobby] Failed to create Steam lobby — CreateLobbyAsync returned null.");
                return false;
            }

            CurrentLobby = createdLobby.Value;
            CurrentLobby.SetPublic();
            CurrentLobby.SetJoinable(true);
            CurrentLobby.SetData("host", SteamClient.Name);

            // Host targets self for Facepunch transport.
            facepunch.targetSteamId = SteamClient.SteamId;
            Debug.Log($"[CreateLobby] Steam lobby created: {CurrentLobby.Id}, targetSteamId={facepunch.targetSteamId}");

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                Debug.Log("[CreateLobby] Calling StartHost...");
                if (!NetworkManager.Singleton.StartHost())
                {
                    Debug.LogError("[CreateLobby] Failed to start NGO host.");
                    CurrentLobby.Leave();
                    CurrentLobby = default;
                    return false;
                }
                Debug.Log("[CreateLobby] StartHost succeeded.");
            }
            else
            {
                Debug.Log($"[CreateLobby] Skipping StartHost — already IsHost={NetworkManager.Singleton.IsHost} IsServer={NetworkManager.Singleton.IsServer}");
            }

            OnLobbyUpdated?.Invoke();
            Debug.Log($"[CreateLobby] Done. Lobby: {CurrentLobby.Id}");
            return true;
        }
        else if (transport is UnityTransport)
        {
            Debug.Log("Starting LAN Host via UnityTransport");

            if (!NetworkManager.Singleton.IsHost && !NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.StartHost();
            }

            return true;
        }

        return false;
    }

    // =========================
    // CLIENT
    // =========================

    /// <summary>Joins a Steam lobby by ID (Facepunch transport) or connects to a LAN host at 127.0.0.1 (UnityTransport).
    /// Pass lobbyId = 0 when using UnityTransport to fall through to the LAN path.</summary>
    public async void JoinLobby(ulong lobbyId) => await JoinLobbyInternal(lobbyId, "127.0.0.1");

    /// <summary>Joins a LAN host at the given IP address using UnityTransport.
    /// Ignored when FacepunchTransport is the active transport.</summary>
    public async void JoinLobbyLAN(string address) => await JoinLobbyInternal(0, address);

    private async Task JoinLobbyInternal(ulong lobbyId, string lanAddress)
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
                Debug.LogError($"[JoinLobby] Failed to join lobby {lobbyId}. Result: {joinResult}");
                OnJoinFailed?.Invoke(joinResult.ToString());
                return;
            }

            CurrentLobby = lobby;

            if (facepunchTransport == null)
            {
                Debug.LogError("[JoinLobby] facepunchTransport is null — cannot connect.");
                return;
            }

            facepunchTransport.targetSteamId = CurrentLobby.Owner.Id;
            Debug.Log($"[JoinLobby] Steam join success. targetSteamId={facepunchTransport.targetSteamId}");

            if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
            {
                bool started = NetworkManager.Singleton.StartClient();
                Debug.Log($"[JoinLobby] StartClient={started}");
            }
            else
            {
                Debug.LogWarning($"[JoinLobby] Skipping StartClient — already IsClient={NetworkManager.Singleton.IsClient} IsHost={NetworkManager.Singleton.IsHost}");
            }
        }
        else if (transport is UnityTransport unityTransport)
        {
            unityTransport.SetConnectionData(lanAddress, 7777);
            Debug.Log($"[JoinLobby] Connecting to LAN host at {lanAddress}:7777");

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

        Debug.Log($"[OnLobbyEntered] IsHost={NetworkManager.Singleton.IsHost} IsClient={NetworkManager.Singleton.IsClient} Members={CurrentLobby.Members.Count()} OwnerSteamId={lobby.Owner.Id}");

        OnLobbyUpdated?.Invoke();
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

        Debug.Log($"[Host] OnClientConnected clientId={clientId} GameManager.Instance={GameManager.Instance != null}");

        if (CurrentLobby.Id != 0)
            Debug.Log($"[Host] Steam members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();

        if (GameManager.Instance == null)
        {
            Debug.LogError("[Host] OnClientConnected: GameManager.Instance is null — cannot spawn or send RPC.");
            return;
        }

        Debug.Log($"[Host] HasGameStarted={GameManager.Instance.HasGameStarted} IsTransitioningToLobby={GameManager.Instance.IsTransitioningToLobby}");

        if (GameManager.Instance.HasGameStarted)
        {
            // Spawn relative to where the host currently is.
            bool hostIsOutside = PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutside;
            if (hostIsOutside)
            {
                Debug.Log($"[Host] Game started, host is outside — spawning client at lobby for clientId={clientId}");
                GameManager.Instance.SpawnPlayerAtLobbyServer(clientId);
            }
            else
            {
                Debug.Log($"[Host] Game started, host is at booth — spawning client at booth for clientId={clientId}");
                PlayerSpawner.Instance.SpawnPlayerAtBooth(clientId);
            }
            GameManager.Instance.InitializeLateJoinClient(clientId);
        }
        else if (!GameManager.Instance.IsTransitioningToLobby)
        {
            Debug.Log($"[Host] Spawning lobby joiner for clientId={clientId}");
            GameManager.Instance.SpawnPlayerAtLobbyServer(clientId);
            GameManager.Instance.InitializeLobbyJoinClient(clientId);
        }
        else
        {
            Debug.Log($"[Host] IsTransitioningToLobby=true — spawn deferred to LobbyTransitionSequence for clientId={clientId}");
        }
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