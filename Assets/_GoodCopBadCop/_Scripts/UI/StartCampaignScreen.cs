using System;
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

    // Called by join-with-code logic AFTER lobby.Join()
    public void SetCurrentLobby(Lobby lobby)
    {
        currentLobby = lobby;
        Debug.Log("Client entered lobby");

        RefreshLobbyUI();

        NetworkManager.Singleton.StartClient();
    }

    // =========================
    // CALLBACKS
    // =========================

    // Client-side signal that Steam lobby entry completed
    private void OnLobbyEntered(Lobby lobby)
    {
        if (NetworkManager.Singleton.IsHost)
            return;

        currentLobby = lobby;
        Debug.Log("Steam lobby entered (client)");

        RefreshLobbyUI();
    }

    // Host-side signal that Netcode client connected
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

        // Allow Steam to settle membership
        await Task.Delay(50);

        var members = currentLobby.Members;
        int count = members.Count();

        playerOneInfoPanel.gameObject.SetActive(false);
        playerTwoInfoPanel.gameObject.SetActive(false);

        int i = 0;
        foreach (var member in currentLobby.Members)
        {
            if (i == 0)
            {
                playerOneInfoPanel.PopulateInfo(member.Name);
                playerOneInfoPanel.gameObject.SetActive(true);
            }
            else if (i == 1)
            {
                playerTwoInfoPanel.PopulateInfo(member.Name);
                playerTwoInfoPanel.gameObject.SetActive(true);
            }
            i++;
        }

        // Host-only: enable Start when both players present
        if (NetworkManager.Singleton.IsHost)
        {
            startButton.SetActive(count >= 2);
            waitForHostText.SetActive(false);
        }
        else
        {
            startButton.SetActive(false);
            waitForHostText.SetActive(true);
        }
    }
}
