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

    private void Awake()
    {
        // Gamepad focus: Restart Day first (when shown), then Spectate, then Back to Menu.
        GamepadMenuNavigator.EnsureOn(gameObject, true,
            FindButton(restartDayButton), FindButton(spectateButton), FindButton(backToMenuButton));
    }

    private static UnityEngine.UI.Button FindButton(GameObject go) =>
        go != null ? go.GetComponentInChildren<UnityEngine.UI.Button>(true) : null;

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

        RefreshSpectateButton();
    }

    private void RefreshSpectateButton()
    {
        if (spectateButton == null) return;

        bool canSpectate = HasAliveTeammate();
        if (spectateButton.activeSelf != canSpectate)
            spectateButton.SetActive(canSpectate);
    }

    /// <summary>
    /// Returns true when at least one teammate (not the local player) is spawned and alive,
    /// i.e. there is someone to spectate. Uses the same validity rules as SpectateManager.
    /// </summary>
    private bool HasAliveTeammate()
    {
        return SpectateManager.Instance != null && SpectateManager.Instance.SpectatableCount > 0;
    }

    // Poll so the Spectate button also tracks teammates that spawn, revive or die after the
    // death screen opened (OnDeath subscriptions only cover players present at OnEnable).
    private const float SpectateRefreshInterval = 0.5f;
    private float _spectateRefreshTimer;

    private void Update()
    {
        _spectateRefreshTimer -= Time.unscaledDeltaTime;
        if (_spectateRefreshTimer > 0f) return;

        _spectateRefreshTimer = SpectateRefreshInterval;
        RefreshSpectateButton();
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
    //
    // Also listens for client disconnects so the Spectate button deactivates
    // if the teammate (e.g. the host) leaves the server while the death
    // screen is still up.
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

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
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

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    /// <summary>
    /// Invoked when any client (including the host) disconnects. Re-evaluates
    /// button visibility so the Spectate button disappears if the teammate it
    /// was pointing at just left the server.
    /// </summary>
    private void OnClientDisconnect(ulong clientId)
    {
        RefreshButtonVisibility();
    }

    // -------------------------------------------------------------------------
    // Button Handlers
    // -------------------------------------------------------------------------

    /// <summary>Called by the Spectate button's OnClick event in the Inspector.</summary>
    public void OnSpectateClicked()
    {
        if (!HasAliveTeammate())
        {
            RefreshSpectateButton();
            return;
        }

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
