using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using TMPro;

public class JoinLobbyScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField inviteCodeInput;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private GameObject connectingAnimation;

    private const string ErrorEmptyCode     = "NO CODE ENTERED";
    private const string ErrorInvalidCode   = "INVALID JOIN CODE";
    private const string ErrorLobbyNotFound = "LOBBY NOT FOUND";
    private const string ErrorLobbyFull     = "LOBBY FULL";
    private const string ErrorSteamNotReady = "STEAM NOT CONNECTED";
    private const string ErrorJoinFailed    = "UNABLE TO JOIN LOBBY";
    private const string ErrorInvalidIP     = "INVALID IP ADDRESS";
    private const string ErrorHostNoResponse = "HOST DID NOT RESPOND";
    private const string StatusLANReady     = "READY — PRESS JOIN TO CONNECT";
    private const string StatusJoiningInvite = "JOINING FRIEND'S LOBBY...";

    private void Awake()
    {
        inviteCodeInput.onSubmit.AddListener(_ => JoinWithCode());
        inviteCodeInput.onValueChanged.AddListener(_ => ClearStatus());
    }

    private void OnEnable()
    {
        LobbyManager.Instance.OnJoinFailed += OnJoinFailed;
        inviteCodeInput.interactable = true;
        SetConnectingAnimationVisible(false);
        RefreshUI();
    }

    private void OnDisable()
    {
        LobbyManager.Instance.OnJoinFailed -= OnJoinFailed;
        SetConnectingAnimationVisible(false);
    }

    /// <summary>Cold-launch Steam invite: shows the connecting state with code entry locked.</summary>
    public void ShowInviteJoining()
    {
        inviteCodeInput.interactable = false;
        SetConnectingAnimationVisible(true);
        SetStatus(StatusJoiningInvite);
    }

    /// <summary>Cold-launch Steam invite failed: shows the reason and unlocks code entry.</summary>
    public void ShowInviteJoinFailed(string reason)
    {
        inviteCodeInput.interactable = true;
        OnJoinFailed(reason);
    }

    /// <summary>
    /// Reads the input field and joins via the active transport:
    /// FacepunchTransport — expects a 6-character join code.
    /// UnityTransport     — connects immediately; no code required.
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
        SetConnectingAnimationVisible(true);
        LobbyManager.Instance.JoinLobbyByCode(code);
    }

    /// <summary>
    /// Connects to a LAN host. When UnityTransport is active the input field is hidden,
    /// so <paramref name="raw"/> will always be empty and the default address is used.
    /// </summary>
    private void JoinLAN(string raw)
    {
        string address = string.IsNullOrEmpty(raw) ? "127.0.0.1" : raw;

        if (!IsValidIPv4(address))
        {
            SetStatus(ErrorInvalidIP);
            return;
        }

        Debug.Log($"[JoinLobbyScreen] Joining LAN host at {address}");
        SetConnectingAnimationVisible(true);
        LobbyManager.Instance.JoinLobbyLAN(address);
    }

    private void OnJoinFailed(string reason)
    {
        Debug.LogWarning($"Join failed: {reason}");
        SetConnectingAnimationVisible(false);
        SetStatus(reason switch
        {
            "CODE_NOT_FOUND"  => ErrorLobbyNotFound,
            "STEAM_NOT_READY" => ErrorSteamNotReady,
            "LobbyFull"       => ErrorLobbyFull,
            "Full"            => ErrorLobbyFull,
            "DoesntExist"     => ErrorLobbyNotFound,
            "TIMEOUT"         => ErrorHostNoResponse,
            _                 => ErrorJoinFailed,
        });
    }

    private void SetConnectingAnimationVisible(bool visible)
    {
        if (connectingAnimation != null)
            connectingAnimation.SetActive(visible);
    }

    private void SetStatus(string message) => statusLabel.text = message;

    private void ClearStatus() => statusLabel.text = string.Empty;

    /// <summary>
    /// Refreshes the UI to match the active transport.
    /// UnityTransport: hides the code input and shows a ready prompt.
    /// FacepunchTransport: shows the code input with appropriate placeholder.
    /// </summary>
    private void RefreshUI()
    {
        if (NetworkManager.Singleton == null)
            return;

        bool isUnityTransport = NetworkManager.Singleton.NetworkConfig.NetworkTransport is UnityTransport;

        if (isUnityTransport)
        {
            SetStatus(StatusLANReady);
        }
        else
        {
            ClearStatus();
            UpdatePlaceholder();
        }
    }

    /// <summary>Updates the input field placeholder text when using FacepunchTransport.</summary>
    private void UpdatePlaceholder()
    {
        var placeholder = inviteCodeInput.placeholder as TMP_Text;
        if (placeholder != null)
            placeholder.text = "Enter join code (e.g. ABC123)";
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
