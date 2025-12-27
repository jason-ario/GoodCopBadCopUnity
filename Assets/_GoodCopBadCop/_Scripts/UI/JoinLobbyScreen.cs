using System;
using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private MainMenuController mainMenuController;

    private void Awake()
    {
        // Press Enter to join
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
    }

    public void JoinWithCode()
    {
        string code = inviteCodeInput.text
            .Trim()
            .Replace("-", "")
            .Replace(" ", "")
            .ToUpper();

        if (string.IsNullOrEmpty(code))
        {
            Debug.LogWarning("Invite code is empty");
            return;
        }

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

        Debug.Log($"Joining lobby {lobbyId}");

        // 🔑 ALL networking handled by LobbyManager
        LobbyManager.Instance.JoinLobby(lobbyId);

        // ✅ UI only — client waiting screen
        mainMenuController.OpenStartCampaignAsClient();
    }
}