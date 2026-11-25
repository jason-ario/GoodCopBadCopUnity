using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Host-only button that flips the Steam lobby between private (join code / invite only) and
/// public (also listed in the Join Game "Public Lobbies" tab). Owners call <see cref="Refresh"/>
/// (pre-game lobby screen, pause menu); it hides itself for clients and when there is no Steam lobby.
/// </summary>
public class LobbyVisibilityToggle : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    // English source text; render-time localization translates code-set labels.
    private static readonly string PrivateLabel = Loc.N("Lobby: Private");
    private static readonly string PublicLabel = Loc.N("Lobby: Public");

    private bool _listening;

    private void Awake() => EnsureListening();

    private void EnsureListening()
    {
        if (_listening || button == null) return;
        button.onClick.AddListener(Toggle);
        _listening = true;
    }

    /// <summary>Shows the toggle only for the lobby owner (and when <paramref name="allowed"/>), and syncs its label.</summary>
    public void Refresh(bool allowed = true)
    {
        LobbyManager lobby = LobbyManager.Instance;
        bool show = allowed && lobby != null && lobby.CanChangeLobbyVisibility;

        if (gameObject.activeSelf != show)
            gameObject.SetActive(show);

        if (show)
        {
            EnsureListening();
            UpdateLabel();
        }
    }

    private void Toggle()
    {
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby == null || !lobby.CanChangeLobbyVisibility) return;

        lobby.SetLobbyPublic(!lobby.IsLobbyPublic);
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (label == null || LobbyManager.Instance == null) return;
        label.text = LobbyManager.Instance.IsLobbyPublic ? PublicLabel : PrivateLabel;
    }
}
