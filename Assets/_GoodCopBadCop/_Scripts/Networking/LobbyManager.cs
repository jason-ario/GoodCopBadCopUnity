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

    public event System.Action OnLobbyUpdated;
    public event System.Action OnKicked;

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
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberJoined;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }
    
    // =========================
    // HOST
    // =========================
    public async void CreateLobby()
    {
        if (!NetworkManager.Singleton.StartHost())
            return;

        var lobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);

        lobby.SetPublic();
        lobby.SetJoinable(true);
        lobby.SetData("host", SteamClient.Name);

        if (NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport facepunch)
        {
            facepunch.targetSteamId = lobby.Owner.Id;
        }
    }

    // =========================
    // CLIENT
    // =========================
    public async void JoinLobby(ulong lobbyId)
    {
        var lobby = new Lobby(lobbyId);
        await lobby.Join();

        CurrentLobby = lobby;

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is FacepunchTransport facepunch)
        {
            // 🔑 THIS is the missing piece
            facepunch.targetSteamId = lobby.Owner.Id;
        }
        else if (transport is UnityTransport unityTransport)
        {
            // LAN fallback
            unityTransport.SetConnectionData("127.0.0.1", 7777);
        }

        // 🔥 Connect to host Netcode server
        NetworkManager.Singleton.StartClient();
    }


    // =========================
    // STEAM CALLBACKS
    // =========================

    /// <summary>
    /// Fires ONLY for the local player (host or client)
    /// </summary>
    private async void OnLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;

        await Task.Delay(50); // Steam membership settle

        Debug.Log($"[OnLobbyEntered] Local members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();

        if (!NetworkManager.Singleton.IsHost)
            NetworkManager.Singleton.StartClient();
    }

    /// <summary>
    /// 🔑 THIS is what fires on the HOST when a client joins
    /// </summary>
    private async void OnLobbyMemberJoined(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50); // Steam updates members slightly later

        Debug.Log($"[OnLobbyMemberJoined] {friend.Name}");
        Debug.Log($"Members now: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();
    }

    private async void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

        await Task.Delay(50);

        ExitLobby();
        
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
        Debug.Log($"[Host] Netcode client connected. Steam members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();
    }

    // =========================
    // HELPERS
    // =========================
    public Steamworks.Friend[] GetMembersSnapshot()
    {
        return CurrentLobby.Members.ToArray();
    }

    public bool IsHost => NetworkManager.Singleton.IsHost;

    public void ExitLobby()
    {
        if (IsHost)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        CurrentLobby.Leave();
    }

}
