using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;

    private const string ErrorEmptyCode = "NO CODE ENTERED";
    private const string ErrorInvalidCode = "INVALID LOBBY ID";
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

    /// <summary>Parses the entered lobby ID and joins the corresponding Steam lobby directly.</summary>
    public void JoinWithCode()
    {
        string raw = inviteCodeInput.text.Trim();

        if (string.IsNullOrEmpty(raw))
        {
            SetStatus(ErrorEmptyCode);
            return;
        }

        if (!ulong.TryParse(raw, out ulong lobbyId) || lobbyId == 0)
        {
            SetStatus(ErrorInvalidCode);
            return;
        }

        Debug.Log($"Joining lobby with ID: {lobbyId}");
        LobbyManager.Instance.JoinLobby(lobbyId);
    }

    private void OnJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        SetStatus(ErrorLobbyNotFound);
    }

    private void SetStatus(string message) => statusLabel.text = message;

    private void ClearStatus() => statusLabel.text = string.Empty;
}