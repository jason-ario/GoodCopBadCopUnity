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

        // This ALSO joins the lobby
        var lobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);

        lobby.SetPublic();
        lobby.SetJoinable(true);
        lobby.SetData("host", SteamClient.Name);

        string inviteCode = InviteCodeUtility.EncodeLobbyId(lobby.Id);
        inviteCodeText.text = $"Invite Code: {inviteCode}";
    }

    // =========================
    // CLIENT FLOW
    // =========================
    public void OpenAsClient()
    {
        startButton.SetActive(false);
        waitForHostText.SetActive(true);
    }

    // Called after lobby.Join()
    public void SetCurrentLobby(Lobby lobby)
    {
        currentLobby = lobby;
        Debug.Log("Client joined lobby via code");
        // Netcode will be started in OnLobbyEntered
    }

    // =========================
    // CALLBACKS
    // =========================

    // 🔑 AUTHORITATIVE lobby snapshot (HOST + CLIENT)
    private void OnLobbyEntered(Lobby lobby)
    {
        currentLobby = lobby;

        Debug.Log($"Lobby entered. Members: {currentLobby.Members.Count()}");

        RefreshLobbyUI();

        // Client only: start networking AFTER joining lobby
        if (!NetworkManager.Singleton.IsHost)
        {
            NetworkManager.Singleton.StartClient();
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        Debug.Log($"Netcode client connected: {clientId}");
        RefreshLobbyUI();
    }

    private void OnLobbyMemberLeave(Lobby lobby, Friend friend)
    {
        if (currentLobby.Id == 0 || lobby.Id != currentLobby.Id)
            return;

        RefreshLobbyUI();
    }

    // =========================
    // UI
    // =========================
    private async void RefreshLobbyUI()
    {
        if (currentLobby.Id == 0)
            return;

        await Task.Delay(50); // Steam settle

        var members = currentLobby.Members.ToArray();

        playerOneInfoPanel.gameObject.SetActive(false);
        playerTwoInfoPanel.gameObject.SetActive(false);

        if (members.Length > 0)
        {
            playerOneInfoPanel.PopulateInfo(members[0].Name);
            playerOneInfoPanel.gameObject.SetActive(true);
        }

        if (members.Length > 1)
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

        Debug.Log($"[UI] Members count = {members.Length}");
    }
}
