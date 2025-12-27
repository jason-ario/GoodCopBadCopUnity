using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance;

    public Lobby CurrentLobby { get; private set; }

    public event System.Action OnLobbyUpdated;
    public event System.Action OnClientJoined;

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
        
        OnLobbyUpdated?.Invoke();
        var members = LobbyManager.Instance.GetMembersSnapshot();
        foreach (var friend in members)
        {
            Debug.Log(friend.Name);
        }
    }

    // =========================
    // CLIENT
    // =========================
    public async void JoinLobby(ulong lobbyId)
    {
        var lobby = new Lobby(lobbyId);
        await lobby.Join();
        // Netcode will start after OnLobbyEntered
    }

    // =========================
    // CALLBACKS
    // =========================
    private void OnLobbyEntered(Lobby lobby)
    {
        CurrentLobby = lobby;

        Debug.Log($"Lobby entered. Members: {CurrentLobby.Members.Count()}");
        
        var members = LobbyManager.Instance.GetMembersSnapshot();
        foreach (var friend in members)
        {
            Debug.Log(friend.Name);
        }
        
        OnLobbyUpdated?.Invoke();

        if (!NetworkManager.Singleton.IsHost)
            NetworkManager.Singleton.StartClient();
    }
    
    
    private async void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        // Steam membership updates slightly after netcode connect
        await Task.Delay(50);

        Debug.Log($"[Host] Client connected. Steam members: {CurrentLobby.Members.Count()}");

        OnLobbyUpdated?.Invoke();
    }
    private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (CurrentLobby.Id == 0 || lobby.Id != CurrentLobby.Id)
            return;

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
}
