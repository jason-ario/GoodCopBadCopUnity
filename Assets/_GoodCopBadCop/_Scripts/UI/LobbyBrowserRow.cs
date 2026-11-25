using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// One row in the Join Game public lobby browser: host name, build version, player count and a
/// join button. Rows whose build version differs from the local build can't be joined.
/// </summary>
public class LobbyBrowserRow : MonoBehaviour
{
    // English source text; render-time localization translates the button label when shown.
    private static readonly string JoinText     = Loc.N("JOIN");
    private static readonly string MismatchText = Loc.N("WRONG VERSION");

    [SerializeField] private TMP_Text hostNameText;
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private TMP_Text playerCountText;
    [Tooltip("Black bar button; its ColorTint transition handles hover (orange) and disabled (grey).")]
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text joinButtonLabel;

    private bool _canJoin;

    /// <summary>Raw (unlocalized) host name, used by the browser's search filter.</summary>
    public string HostName { get; private set; } = string.Empty;

    /// <summary>Steam lobby this row joins.</summary>
    public ulong LobbyId { get; private set; }

    /// <summary>
    /// Fills the row. <paramref name="hostName"/>, <paramref name="version"/> and
    /// <paramref name="playerCount"/> are shown verbatim (never localized).
    /// </summary>
    public void Populate(ulong lobbyId, string hostName, string version, string playerCount, bool versionMatches, UnityAction onJoin)
    {
        LobbyId = lobbyId;
        HostName = hostName ?? string.Empty;
        _canJoin = versionMatches;

        // The template is translated; the Steam name inside it never goes through localization.
        hostNameText.text = Loc.Verbatim(Loc.T("{0}'s Lobby", HostName));
        if (versionText != null)
            versionText.text = Loc.Verbatim(string.IsNullOrEmpty(version) ? "-" : $"v{version}");
        playerCountText.text = Loc.Verbatim(playerCount);

        if (joinButtonLabel != null) joinButtonLabel.text = versionMatches ? JoinText : MismatchText;

        joinButton.onClick.RemoveAllListeners();
        joinButton.onClick.AddListener(onJoin);
        SetInteractable(true);
    }

    /// <summary>Enables/disables joining; version-mismatched rows always stay disabled.</summary>
    public void SetInteractable(bool interactable) => joinButton.interactable = interactable && _canJoin;

    /// <summary>Case-insensitive host-name search match. An empty query matches everything.</summary>
    public bool Matches(string query) =>
        string.IsNullOrEmpty(query) || HostName.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
}
