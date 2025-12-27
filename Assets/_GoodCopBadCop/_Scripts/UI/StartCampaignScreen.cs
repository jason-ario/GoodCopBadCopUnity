using System;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

public class StartCampaignScreen : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI inviteCodeText;
    [SerializeField] PlayerInfoPanel playerOneInfoPanel;
    [SerializeField] PlayerInfoPanel playerTwoInfoPanel;
    [SerializeField] GameObject startButton;
    [SerializeField] GameObject waitForHostText;

    private Lobby currentLobby;

    #region Unity lifecycle

    private void OnEnable()
    {
        SteamMatchmaking.OnLobbyMemberJoined += OnLobbyMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave += OnLobbyMemberChanged;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
    }

    private void OnDisable()
    {
        SteamMatchmaking.OnLobbyMemberJoined -= OnLobbyMemberChanged;
        SteamMatchmaking.OnLobbyMemberLeave -= OnLobbyMemberChanged;

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

        // 1️⃣ Start Netcode Host
        NetworkManager.Singleton.StartHost();

        // 2️⃣ Create Steam Lobby
        currentLobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);

        // 3️⃣ Make it joinable
        currentLobby.SetFriendsOnly(); // or SetPublic()
        currentLobby.SetJoinable(true);

        // 4️⃣ Store metadata
        currentLobby.SetData("host", SteamClient.Name);

        // 5️⃣ Invite code
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

    // =========================
    // CALLBACKS
    // =========================
    private void OnLobbyMemberChanged(Lobby lobby, Friend friend)
    {
        if (currentLobby.Id == 0 || lobby.Id != currentLobby.Id)
            return;

        Debug.Log("Steam lobby updated");
        RefreshLobbyUI();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Netcode client connected: {clientId}");
        RefreshLobbyUI();
    }

    // =========================
    // UI
    // =========================
    private void RefreshLobbyUI()
    {
        if (currentLobby.Id == 0)
            return;

        int i = 0;
        
        playerOneInfoPanel.gameObject.SetActive(false);
        playerTwoInfoPanel.gameObject.SetActive(false);

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
    }
}
