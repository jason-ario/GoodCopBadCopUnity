using System;
using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;

    private const string ErrorEmptyCode = "NO CODE ENTERED";
    private const string ErrorInvalidCode = "INVALID ASSIGNMENT CODE";

    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
        inviteCodeInput.onValueChanged.AddListener(_ => ClearStatus());
    }

    /// <summary>Attempts to decode the entered invite code and join the corresponding lobby.</summary>
    public void JoinWithCode()
    {
        string code = inviteCodeInput.text
            .Trim()
            .Replace("-", "")
            .Replace(" ", "")
            .ToUpper();

        if (string.IsNullOrEmpty(code))
        {
            SetStatus(ErrorEmptyCode);
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
            SetStatus(ErrorInvalidCode);
            return;
        }

        Debug.Log($"Joining lobby {lobbyId}");

        // Networking and scene transition handled by LobbyManager
        LobbyManager.Instance.JoinLobby(lobbyId);
    }

    private void SetStatus(string message) => statusLabel.text = message;

    private void ClearStatus() => statusLabel.text = string.Empty;
}