using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only helper for quick two-editor-window multiplayer testing via UnityTransport (LAN).
/// Press Ctrl+Shift+H to start as host, Ctrl+Shift+J to start as client.
/// The active NetworkTransport is swapped to UnityTransport automatically.
/// </summary>
[InitializeOnLoad]
public static class QuickConnect
{
    private const string HostMenuPath = "Tools/Quick Connect/Host %#h";
    private const string ClientMenuPath = "Tools/Quick Connect/Join as Client %#j";

    private const string Host = "127.0.0.1";
    private const ushort Port = 7777;

    static QuickConnect()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
            EditorApplication.update += WaitForNetworkManager;
        else if (state == PlayModeStateChange.ExitingPlayMode)
            EditorApplication.update -= WaitForNetworkManager;
    }

    private static void WaitForNetworkManager()
    {
        if (NetworkManager.Singleton == null) return;
        EditorApplication.update -= WaitForNetworkManager;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnectedToHost;
    }

    // =========================
    // HOST
    // =========================
    [MenuItem(HostMenuPath)]
    private static void StartHost()
    {
        if (!ValidatePlayMode()) return;

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[QuickConnect] NetworkManager.Singleton is null.");
            return;
        }

        if (nm.IsListening)
        {
            Debug.LogWarning("[QuickConnect] NetworkManager is already listening.");
            return;
        }

        SwapToUnityTransport(nm);
        nm.StartHost();

        Debug.Log($"[QuickConnect] Host listening on {Host}:{Port} — waiting for client...");
    }

    private static void OnClientConnectedToHost(ulong clientId)
    {
        var nm = NetworkManager.Singleton;

        // Ignore the host's own connection event.
        if (!nm.IsServer || clientId == nm.LocalClientId) return;

        var gm = GameManager.Instance;
        if (gm == null)
        {
            Debug.LogWarning("[QuickConnect] GameManager not found.");
            return;
        }

        if (gm.HasGameStarted)
        {
            Debug.Log($"[QuickConnect] Late join — spawning player for client {clientId}.");
            PlayerSpawner.Instance.SpawnPlayer(clientId, isSinglePlayer: false);
            gm.InitializeLateJoinClient(clientId);
        }
        else
        {
            nm.OnClientConnectedCallback -= OnClientConnectedToHost;
            Debug.Log($"[QuickConnect] Client {clientId} connected — starting game.");
            gm.TryStartGame(skipTransition: true);
        }
    }

    // =========================
    // CLIENT
    // =========================
    [MenuItem(ClientMenuPath)]
    private static void StartClient()
    {
        if (!ValidatePlayMode()) return;

        var nm = NetworkManager.Singleton;
        if (nm == null)
        {
            Debug.LogError("[QuickConnect] NetworkManager.Singleton is null.");
            return;
        }

        if (nm.IsListening)
        {
            Debug.LogWarning("[QuickConnect] NetworkManager is already listening.");
            return;
        }

        SwapToUnityTransport(nm);

        if (LobbyManager.Instance != null)
        {
            LobbyManager.Instance.JoinLobby(0);
        }
        else
        {
            var transport = nm.GetComponent<UnityTransport>();
            transport.SetConnectionData(Host, Port);
            nm.StartClient();
        }

        Debug.Log($"[QuickConnect] Started client, connecting to {Host}:{Port}");
    }

    // =========================
    // VALIDATION
    // =========================
    [MenuItem(HostMenuPath, true)]
    [MenuItem(ClientMenuPath, true)]
    private static bool ValidateMenuItems() => EditorApplication.isPlaying;

    // =========================
    // HELPERS
    // =========================
    /// <summary>
    /// Swaps the active NetworkTransport to UnityTransport and configures the connection data.
    /// The FacepunchTransport remains on the GameObject for when it is needed again.
    /// </summary>
    private static void SwapToUnityTransport(NetworkManager nm)
    {
        var unityTransport = nm.GetComponent<UnityTransport>();
        if (unityTransport == null)
        {
            Debug.LogError("[QuickConnect] No UnityTransport component found on NetworkManager.");
            return;
        }

        unityTransport.SetConnectionData(Host, Port);
        nm.NetworkConfig.NetworkTransport = unityTransport;

        var facepunch = nm.GetComponent<FacepunchTransport>();
        if (facepunch != null)
            facepunch.enabled = false;

        unityTransport.enabled = true;

        Debug.Log("[QuickConnect] Swapped NetworkTransport → UnityTransport");
    }

    private static bool ValidatePlayMode()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[QuickConnect] Enter Play Mode before connecting.");
            return false;
        }

        return true;
    }
}
