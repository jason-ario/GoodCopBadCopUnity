using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;

    private const string ErrorEmptyCode = "NO CODE ENTERED";
    private const string ErrorLobbyNotFound = "LOBBY NOT FOUND";

    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
        inviteCodeInput.onValueChanged.AddListener(_ => ClearStatus());
    }

    private void OnEnable()
    {
        LobbyManager.Instance.OnJoinFailed += OnJoinFailed;
    }

    private void OnDisable()
    {
        LobbyManager.Instance.OnJoinFailed -= OnJoinFailed;
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

        Debug.Log($"Joining lobby with code: {code}");

        // Networking and scene transition handled by LobbyManager
        LobbyManager.Instance.JoinLobbyByCode(code);
    }

    private void OnJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        SetStatus(ErrorLobbyNotFound);
    }

    private void SetStatus(string message) => statusLabel.text = message;

    private void ClearStatus() => statusLabel.text = string.Empty;
}