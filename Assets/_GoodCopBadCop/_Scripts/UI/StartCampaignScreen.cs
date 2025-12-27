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
    [SerializeField] private GameObject startButton;
    [SerializeField] private GameObject waitForHostText;
    
    public async void StartCampaignAsHost()
    {
        Debug.Log("Starting server...");

        // 1️⃣ Start Netcode Host
        NetworkManager.Singleton.StartHost();

        // 2️⃣ Create Steam Lobby
        Lobby lobby = (Lobby)await SteamMatchmaking.CreateLobbyAsync(2);
        
        // 3️⃣ Make it joinable
        lobby.SetFriendsOnly(); // or SetPublic()
        lobby.SetJoinable(true);

        // 4️⃣ Store useful metadata
        lobby.SetData("name", SteamClient.Name);
        lobby.SetData("host", SteamClient.Name);

        // 5️⃣ Display invite code (Lobby ID)
        string shortCode = InviteCodeUtility.EncodeLobbyId(lobby.Id);
        string inviteCode = shortCode;
        Debug.Log($"Invite Code: {inviteCode}");
        inviteCodeText.text = "Invite Code: " + inviteCode;

        // TODO: show this in your UI text field
        playerOneInfoPanel.PopulateInfo(SteamClient.Name);
    }

    public void ClientJoined(string steamClientName)
    {
    }

    public void OpenAsClient()
    {
        startButton.SetActive(false);
        waitForHostText.SetActive(true);
    }
}