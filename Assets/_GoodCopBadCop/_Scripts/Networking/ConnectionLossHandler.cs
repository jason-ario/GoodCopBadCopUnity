using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Detects an unexpected loss of network connection for the local client and restarts the
/// current scene after showing a "Lost connection" notification.
///
/// Voluntary disconnects (leaving the lobby, returning to the main menu, quitting) are
/// ignored via <see cref="LobbyManager.IsIntentionalDisconnect"/>, which LobbyManager sets
/// while it's tearing down the session on purpose.
///
/// Place on a persistent object alongside NetworkManager/LobbyManager, and assign a
/// ConnectionLostNotification in the inspector (e.g. on the main UI canvas).
/// </summary>
public class ConnectionLossHandler : MonoBehaviour
{
    [SerializeField] private ConnectionLostNotification _notification;
    [Tooltip("Seconds to display the notification before restarting the scene.")]
    [SerializeField] private float _delayBeforeRestart = 2.5f;

    private NetworkManager _networkManager;
    private bool _isSubscribed;
    private bool _isHandlingDisconnect;

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void Update()
    {
        // NetworkManager.Singleton can become available/replaced after this component is
        // enabled, so keep trying until subscribed.
        if (!_isSubscribed)
            TrySubscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void TrySubscribe()
    {
        NetworkManager current = NetworkManager.Singleton;
        if (current == null || _isSubscribed)
            return;

        _networkManager = current;
        _networkManager.OnClientDisconnectCallback += OnClientDisconnect;
        _isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_isSubscribed || _networkManager == null)
            return;

        _networkManager.OnClientDisconnectCallback -= OnClientDisconnect;
        _isSubscribed = false;
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (_isHandlingDisconnect)
            return;

        if (LobbyManager.IsIntentionalDisconnect)
            return;

        // Only react to the local client losing its own connection, not other players
        // disconnecting from a session we're hosting.
        bool isLocalClient = _networkManager != null && clientId == _networkManager.LocalClientId;
        bool weAreStillTheServer = _networkManager != null && _networkManager.IsServer;

        if (!isLocalClient || weAreStillTheServer)
            return;

        _isHandlingDisconnect = true;
        StartCoroutine(HandleLostConnection());
    }

    private IEnumerator HandleLostConnection()
    {
        if (_notification != null)
            _notification.Show("Lost connection");
        else
            Debug.LogWarning("[ConnectionLossHandler] No notification assigned — restarting scene without a visible message.");

        yield return new WaitForSecondsRealtime(_delayBeforeRestart);

        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }
}
