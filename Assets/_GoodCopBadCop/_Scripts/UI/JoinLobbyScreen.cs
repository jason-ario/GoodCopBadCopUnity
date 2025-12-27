using System;
using UnityEngine;
using Unity.Netcode;
using Steamworks;
using Steamworks.Data;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] TMP_InputField inviteCodeInput;
    [SerializeField] MainMenuController mainMenuController;
    [SerializeField] StartCampaignScreen startCampaignScreen;
    
    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
    }
    
    public async void JoinWithCode()
    {
        string code = inviteCodeInput.text
            .Trim()
            .Replace("-", "")
            .Replace(" ", "")
            .ToUpper();

        ulong lobbyId;

        try
        {
            lobbyId = InviteCodeUtility.DecodeLobbyId(code);
        }
        catch (Exception e)
        {
            Debug.LogError($"Invalid invite code: {e.Message}");
            return;
        }

        Debug.Log($"Decoded lobby ID: {lobbyId}");

        Lobby lobby = new Lobby(lobbyId);

        Debug.Log("Joining lobby...");
        await lobby.Join();

        Debug.Log("Starting client...");
        NetworkManager.Singleton.StartClient();

        mainMenuController.OpenStartCampaignScreen(true);
    }
}