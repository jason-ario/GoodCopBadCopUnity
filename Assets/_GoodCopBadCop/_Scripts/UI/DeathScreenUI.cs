using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DeathScreenUI : MonoBehaviour
{
    [SerializeField] private GameObject restartDayButton;
    [SerializeField] private GameObject backToMenuButton;
    [SerializeField] private GameObject spectateButton;
    [SerializeField] private TextMeshProUGUI daysSurvivedText;

    private void OnEnable()
    {
        RefreshDaysSurvivedText();
        RefreshButtonVisibility();
        SubscribeToDeathEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFromDeathEvents();
    }

    // -------------------------------------------------------------------------
    // Days Survived Text
    // -------------------------------------------------------------------------

    private void RefreshDaysSurvivedText()
    {
        if (daysSurvivedText == null) return;

        int day = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
        daysSurvivedText.text = day == 1
            ? "You survived 1 day"
            : $"You survived {day} days";
    }

    // -------------------------------------------------------------------------
    // Visibility
    // -------------------------------------------------------------------------

    private void RefreshButtonVisibility()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        bool isSinglePlayer = GameManager.Instance != null && GameManager.Instance.IsSinglePlayer;
        bool allPlayersDead = AreAllPlayersDead();

        if (restartDayButton != null)
            restartDayButton.SetActive(isHost && (isSinglePlayer || allPlayersDead));

        if (backToMenuButton != null)
            backToMenuButton.SetActive(true);

        if (spectateButton != null)
            spectateButton.SetActive(HasTeammate());
    }

    /// <summary>
    /// Returns true when there is at least one other connected player (teammate)
    /// in the party besides the local player.
    /// </summary>
    private bool HasTeammate()
    {
        if (NetworkManager.Singleton == null) return false;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var player = client.PlayerObject.GetComponent<PlayerInstance>();
            if (player != null && player != PlayerInstance.Instance)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true when every connected player's <see cref="PlayerHealth"/> reports dead.
    /// Returns false when at least one player is alive or no clients are connected.
    /// </summary>
    private bool AreAllPlayersDead()
    {
        if (NetworkManager.Singleton == null) return false;

        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0) return false;

        foreach (var client in clients)
        {
            PlayerHealth health = GetClientHealth(client);
            if (health != null && !health.IsDead)
                return false;
        }

        return true;
    }

    private PlayerHealth GetClientHealth(NetworkClient client)
    {
        if (client.PlayerObject == null) return null;
        return client.PlayerObject.GetComponent<PlayerHealth>();
    }

    // -------------------------------------------------------------------------
    // Death event subscriptions
    // Keeps the Restart Day button visible state updated while the death screen
    // is open — e.g. the host dies first (button hidden) and later the second
    // player dies, which should reveal the button for the host.
    // -------------------------------------------------------------------------

    private void SubscribeToDeathEvents()
    {
        if (NetworkManager.Singleton == null) return;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerHealth health = GetClientHealth(client);
            if (health != null)
                health.OnDeath += RefreshButtonVisibility;
        }
    }

    private void UnsubscribeFromDeathEvents()
    {
        if (NetworkManager.Singleton == null) return;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            PlayerHealth health = GetClientHealth(client);
            if (health != null)
                health.OnDeath -= RefreshButtonVisibility;
        }
    }

    // -------------------------------------------------------------------------
    // Button Handlers
    // -------------------------------------------------------------------------

    /// <summary>Called by the Spectate button's OnClick event in the Inspector.</summary>
    public void OnSpectateClicked()
    {
        gameObject.SetActive(false);
        PlayerInstance.Instance?.StartSpectating();
    }

    /// <summary>
    /// Called by the Restart Day button's OnClick event in the Inspector.
    /// Host-only: reloads the scene for all connected players and restarts the
    /// current day from the host's save file.
    /// </summary>
    public void OnRestartDayClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;
        GameManager.Instance?.RestartDay();
    }

    /// <summary>Called by the Back to Menu button's OnClick event in the Inspector.</summary>
    public void OnBackToMenuClicked()
    {
        ReturnToMainMenuAsync();
    }

    private async void ReturnToMainMenuAsync()
    {
        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.ExitLobbyAsync();

        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }
}
