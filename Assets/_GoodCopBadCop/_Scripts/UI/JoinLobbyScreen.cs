using Netcode.Transports.Facepunch;
using Unity.Netcode;
using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;

    private const string ErrorEmptyCode     = "NO CODE ENTERED";
    private const string ErrorInvalidCode   = "INVALID JOIN CODE";
    private const string ErrorLobbyNotFound = "LOBBY NOT FOUND";
    private const string ErrorInvalidIP     = "INVALID IP ADDRESS";

    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
        inviteCodeInput.onValueChanged.AddListener(_ => ClearStatus());
    }

    private void OnEnable()
    {
        LobbyManager.Instance.OnJoinFailed += OnJoinFailed;
        UpdatePlaceholder();
    }

    private void OnDisable()
    {
        LobbyManager.Instance.OnJoinFailed -= OnJoinFailed;
    }

    /// <summary>
    /// Reads the input field and joins via the active transport:
    /// FacepunchTransport — expects a 6-character join code.
    /// UnityTransport     — expects an IPv4 address string; defaults to 127.0.0.1 if empty.
    /// </summary>
    public void JoinWithCode()
    {
        if (NetworkManager.Singleton == null)
            return;

        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        string raw = inviteCodeInput.text.Trim();

        if (transport is FacepunchTransport)
        {
            JoinSteam(raw);
        }
        else
        {
            JoinLAN(raw);
        }
    }

    private void JoinSteam(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            SetStatus(ErrorEmptyCode);
            return;
        }

        string code = raw.ToUpperInvariant();

        if (code.Length != 6)
        {
            SetStatus(ErrorInvalidCode);
            return;
        }

        Debug.Log($"[JoinLobbyScreen] Joining Steam lobby by code: {code}");
        LobbyManager.Instance.JoinLobbyByCode(code);
    }

    private void JoinLAN(string raw)
    {
        string address = string.IsNullOrEmpty(raw) ? "127.0.0.1" : raw;

        if (!IsValidIPv4(address))
        {
            SetStatus(ErrorInvalidIP);
            return;
        }

        Debug.Log($"[JoinLobbyScreen] Joining LAN host at {address}");
        LobbyManager.Instance.JoinLobbyLAN(address);
    }

    private void OnJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        SetStatus(ErrorLobbyNotFound);
    }

    private void SetStatus(string message) => statusLabel.text = message;

    private void ClearStatus() => statusLabel.text = string.Empty;

    /// <summary>Updates the input field placeholder text to reflect the active transport.</summary>
    private void UpdatePlaceholder()
    {
        if (NetworkManager.Singleton == null)
            return;

        var placeholder = inviteCodeInput.placeholder as TMP_Text;
        if (placeholder == null)
            return;

        bool isSteam = NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport;
        placeholder.text = isSteam ? "Enter join code (e.g. ABC123)" : "Enter host IP (default: 127.0.0.1)";
    }

    private static bool IsValidIPv4(string address)
    {
        if (string.IsNullOrEmpty(address))
            return false;

        string[] parts = address.Split('.');
        if (parts.Length != 4)
            return false;

        foreach (string part in parts)
        {
            if (!int.TryParse(part, out int value) || value < 0 || value > 255)
                return false;
        }

        return true;
    }
}
