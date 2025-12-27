using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

public class StartCampaignScreen : MonoBehaviour
{
    public static StartCampaignScreen Instance;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI inviteCodeText;
    [SerializeField] private PlayerInfoPanel playerOneInfoPanel;
    [SerializeField] private PlayerInfoPanel playerTwoInfoPanel;
    [SerializeField] private GameObject startButton;
    [SerializeField] private GameObject waitForHostText;

    private Lobby currentLobby;

    #region Unity lifecycle

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberLeave;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDisable()
    {
        SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberLeave;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
    }

    #endregion

    // =========================
    // HOST FLOW
    // =========================
    public async void StartCampaignAsHost()
    {
        Debug.Log("Starting server...");

        if (!NetworkManager.Singleton.StartHost())
        {
            Debug.LogError("Failed to start host");
            return;
        }

        currentLobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);

        // 🔑 REQUIRED — host must enter its own lobby
        SteamMatchmaking.JoinLobbyAsync(currentLobby.Id);

        currentLobby.SetPublic();
        currentLobby.SetJoinable(true);
        currentLobby.SetData("host", SteamClient.Name);

        string inviteCode = InviteCodeUtility.EncodeLobbyId(currentLobby.Id);
        inviteCodeText.text = $"Invite Code: {inviteCode}";
        Debug.Log($"Invite Code: {inviteCode}");

        RefreshLobbyUI();
    }

    // =========================
    // CLIENT FLOW
    // =========================
    public void OpenAsClient()
    {
        startButton.SetActive(false);
        waitForHostText.SetActive(true);
    }

    // Called AFTER lobby.Join()
    public void SetCurrentLobby(Lobby lobby)
    {
        currentLobby = lobby;
        Debug.Log("Client entered lobby");

        // Netcode must start before host detects client
        NetworkManager.Singleton.StartClient();
    }

    // =========================
    // CALLBACKS
    // =========================

    // Client-only: Steam confirms lobby entry
    private void OnLobbyEntered(Lobby lobby)
    {
        if (NetworkManager.Singleton.IsHost)
            return;

        currentLobby = lobby;
        Debug.Log("Steam lobby entered (client)");

        RefreshLobbyUI();
    }

    // Host-only: Netcode client connected
    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        Debug.Log($"Netcode client connected: {clientId}");

        // This is the authoritative signal for host UI
        RefreshLobbyUI();
    }

    private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (currentLobby.Id == 0 || lobby.Id != currentLobby.Id)
            return;

        Debug.Log("Steam lobby member left");
        RefreshLobbyUI();
    }

    // =========================
    // UI
    // =========================
    private async void RefreshLobbyUI()
    {
        if (currentLobby.Id == 0)
            return;

        await Task.Delay(50); // allow Steam to update

        var members = currentLobby.Members.ToList();

        playerOneInfoPanel.gameObject.SetActive(false);
        playerTwoInfoPanel.gameObject.SetActive(false);

        if (members.Count > 0)
        {
            playerOneInfoPanel.PopulateInfo(members[0].Name);
            playerOneInfoPanel.gameObject.SetActive(true);
        }

        if (members.Count > 1)
        {
            playerTwoInfoPanel.PopulateInfo(members[1].Name);
            playerTwoInfoPanel.gameObject.SetActive(true);
        }

        if (NetworkManager.Singleton.IsHost)
        {
            startButton.SetActive(true);
            waitForHostText.SetActive(false);
        }
        else
        {
            startButton.SetActive(false);
            waitForHostText.SetActive(true);
        }

        Debug.Log($"Lobby members count: {members.Count}");
    }
}
