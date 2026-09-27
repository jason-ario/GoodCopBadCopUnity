using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Developer-only console (Ctrl+Shift+O) that lists every public Steam lobby for this app and
/// lets a whitelisted developer (<see cref="DevSpectatorConfig.DevSteamIds"/>) join any of them
/// as a hidden spectator, then cycle through the players in that session.
///
/// Also owns the local spectator presentation: hides menus/HUD, provides a dedicated camera
/// (Camera + CinemachineBrain + AudioListener) that renders whichever player vcam
/// <see cref="SpectateManager"/> activates, and keeps spectating alive as players die/leave.
///
/// Created automatically after the first scene load; persists across scene reloads.
/// </summary>
public class DevSessionBrowser : MonoBehaviour
{
    public static DevSessionBrowser Instance { get; private set; }

    /// <summary>True while the console window is open (spectator mouse cycling is suppressed).</summary>
    public static bool IsOpen => Instance != null && Instance._isOpen;

    private const float ConnectTimeoutSeconds = 20f;
    private const float MaintenanceInterval = 1f;
    private const int WindowId = 0x6C0DE5;

    private bool _isOpen;
    private Rect _windowRect = new Rect(40, 40, 760, 520);
    private Vector2 _scroll;
    private string _status = "";
    private bool _isRefreshing;
    private bool _isJoining;
    private readonly List<Lobby> _lobbies = new List<Lobby>();

    private CursorLockMode _prevLockState;
    private bool _prevCursorVisible;

    // Spectator session state
    private bool _sessionActive;
    private float _connectTimer;
    private float _maintenanceTimer;
    private bool _hideHud = true;
    private GameObject _cameraRig;
    private Camera _rigCamera;
    private AudioListener _rigListener;
    private PlayerInstance _lastCopiedTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;

        var go = new GameObject("[Dev Session Browser]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DevSessionBrowser>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // =========================
    // ACCESS
    // =========================

    /// <summary>Builds require a whitelisted SteamID; the Editor may open it to read your SteamID.</summary>
    private static bool CanOpen => DevSpectatorConfig.IsLocalDev || Application.isEditor;

    private void Update()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && shift && Input.GetKeyDown(KeyCode.O) && CanOpen)
            SetOpen(!_isOpen);

        UpdateSpectatorSession();
    }

    private void SetOpen(bool open)
    {
        if (_isOpen == open) return;
        _isOpen = open;

        if (open)
        {
            _prevLockState = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_lobbies.Count == 0 && !_isRefreshing && DevSpectatorConfig.IsLocalDev)
                _ = RefreshLobbies();
        }
        else
        {
            Cursor.lockState = _prevLockState;
            Cursor.visible = _prevCursorVisible;
        }
    }

    // =========================
    // LOBBY LIST
    // =========================

    private async Task RefreshLobbies()
    {
        if (!SteamClient.IsValid)
        {
            _status = "Steam is not initialized.";
            return;
        }

        _isRefreshing = true;
        _status = "Requesting lobby list...";

        try
        {
            Lobby[] result = await SteamMatchmaking.LobbyList
                .FilterDistanceWorldwide()
                .WithMaxResults(200)
                .RequestAsync();

            ulong ownLobby = LobbyManager.Instance != null ? LobbyManager.Instance.CurrentLobby.Id.Value : 0;

            _lobbies.Clear();
            if (result != null)
                _lobbies.AddRange(result.Where(l => l.Id.Value != ownLobby));

            _status = $"{_lobbies.Count} session(s) found at {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception e)
        {
            _status = $"Lobby request failed: {e.Message}";
            Debug.LogError($"[DevSessionBrowser] {e}");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task SpectateLobby(Lobby lobby)
    {
        if (LobbyManager.Instance == null) return;

        _isJoining = true;
        _status = $"Joining {lobby.GetData("host")} ({lobby.Id}) as hidden spectator...";

        try
        {
            TeardownSession();
            bool ok = await LobbyManager.Instance.JoinLobbyAsDevSpectator(lobby.Id.Value);
            if (ok)
            {
                _connectTimer = ConnectTimeoutSeconds;
                _status = "Joined Steam lobby — connecting to host...";
            }
            else
            {
                _status = "Join failed (see console).";
            }
        }
        finally
        {
            _isJoining = false;
        }
    }

    private async Task LeaveSpectating()
    {
        _status = "Leaving session...";
        TeardownSession();

        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.ExitLobbyAsync();

        // Reload to restore a clean main-menu state (same path as ConnectionLossHandler).
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        _status = "Left session.";
    }

    // =========================
    // SPECTATOR SESSION
    // =========================

    private void UpdateSpectatorSession()
    {
        var nm = NetworkManager.Singleton;

        if (!DevSpectatorRegistry.IsLocalSpectating)
        {
            if (_sessionActive)
                TeardownSession();
            return;
        }

        if (!_sessionActive)
        {
            if (nm != null && nm.IsConnectedClient)
            {
                BeginSession();
            }
            else if (_connectTimer > 0f)
            {
                _connectTimer -= Time.unscaledDeltaTime;
                if (_connectTimer <= 0f)
                {
                    _status = "Timed out connecting to host.";
                    LobbyManager.Instance?.ExitLobby();
                }
            }
            return;
        }

        // Host ended the session / connection dropped.
        if (nm == null || !nm.IsListening || !nm.IsConnectedClient)
        {
            _status = "Disconnected from host.";
            TeardownSession();
            LobbyManager.Instance?.ExitLobby();
            return;
        }

        var spectate = SpectateManager.Instance;
        if (spectate != null)
        {
            if (!spectate.IsSpectating)
                spectate.StartSpectating();

            if (spectate.CurrentTarget != null && spectate.CurrentTarget != _lastCopiedTarget)
                CopyCameraSettingsFrom(spectate.CurrentTarget);
        }

        _maintenanceTimer -= Time.unscaledDeltaTime;
        if (_maintenanceTimer <= 0f)
        {
            _maintenanceTimer = MaintenanceInterval;
            EnforceSingleAudioListener();
            if (_hideHud)
                UIController.Instance?.ClosePlayerUI();
        }
    }

    private void BeginSession()
    {
        _sessionActive = true;
        _maintenanceTimer = 0f;
        _lastCopiedTarget = null;

        var menu = MainMenuController.Instance;
        if (menu != null)
        {
            menu.TransitionToGameplay();
            menu.FadeOutCutsceneMusic();
            menu.StopMainMenuMusic();
        }

        UIController.Instance?.ClosePlayerUI();
        UIController.Instance?.FadeOut();
        AudioManager.Instance?.StartAmbientAudio();

        CreateCameraRig();
        SpectateManager.Instance?.StartSpectating();

        _status = "Spectating (hidden). LMB/RMB or Next/Prev to switch players.";
        Debug.Log("[DevSessionBrowser] Hidden spectator session started.");
    }

    private void TeardownSession()
    {
        if (!_sessionActive && _cameraRig == null) return;

        _sessionActive = false;
        SpectateManager.Instance?.StopSpectating();

        if (_cameraRig != null)
            Destroy(_cameraRig);

        _cameraRig = null;
        _rigCamera = null;
        _rigListener = null;
        _lastCopiedTarget = null;
    }

    private void CreateCameraRig()
    {
        if (_cameraRig != null) return;

        _cameraRig = new GameObject("[Dev Spectator Camera]");
        _cameraRig.transform.SetParent(transform, false);
        _cameraRig.tag = "MainCamera";

        _rigCamera = _cameraRig.AddComponent<Camera>();
        _rigCamera.depth = 100;
        _rigListener = _cameraRig.AddComponent<AudioListener>();
        _cameraRig.AddComponent<CinemachineBrain>();

        var urp = _rigCamera.GetUniversalAdditionalCameraData();
        urp.renderPostProcessing = true;
    }

    /// <summary>Matches lens/culling/post settings to the watched player's own camera.</summary>
    private void CopyCameraSettingsFrom(PlayerInstance target)
    {
        _lastCopiedTarget = target;
        if (_rigCamera == null) return;

        Camera source = target.GetCamera();
        if (source == null) return;

        _rigCamera.CopyFrom(source);
        _rigCamera.depth = 100;
        _rigCamera.targetTexture = null;
        _cameraRig.tag = "MainCamera";

        var srcData = source.GetUniversalAdditionalCameraData();
        var dstData = _rigCamera.GetUniversalAdditionalCameraData();
        dstData.renderPostProcessing = srcData.renderPostProcessing;
        dstData.antialiasing = srcData.antialiasing;
        dstData.antialiasingQuality = srcData.antialiasingQuality;
        dstData.volumeLayerMask = srcData.volumeLayerMask;
        dstData.renderShadows = srcData.renderShadows;
    }

    private void EnforceSingleAudioListener()
    {
        if (_rigListener == null) return;

        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener != _rigListener && listener.enabled)
                listener.enabled = false;
        }

        _rigListener.enabled = true;
    }

    // =========================
    // GUI
    // =========================

    private void OnGUI()
    {
        if (!_isOpen) return;
        _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "Dev Session Browser (Ctrl+Shift+O)");
    }

    private void DrawWindow(int id)
    {
        bool steamValid = SteamClient.IsValid;
        ulong localId = steamValid ? SteamClient.SteamId.Value : 0;

        GUILayout.Label($"Steam: {(steamValid ? $"{SteamClient.Name} ({localId})" : "not initialized")}");

        if (!DevSpectatorConfig.IsLocalDev)
        {
            GUILayout.Label("This SteamID is not whitelisted. Add it to DevSpectatorConfig.DevSteamIds to enable spectating.");
            if (steamValid && GUILayout.Button("Copy my SteamID", GUILayout.Width(160)))
                GUIUtility.systemCopyBuffer = localId.ToString();
            DrawFooter();
            return;
        }

        if (DevSpectatorRegistry.IsLocalSpectating)
            DrawSpectatingPanel();
        else
            DrawLobbyList();

        DrawFooter();
    }

    private void DrawLobbyList()
    {
        GUILayout.BeginHorizontal();
        GUI.enabled = !_isRefreshing && !_isJoining;
        if (GUILayout.Button(_isRefreshing ? "Refreshing..." : "Refresh", GUILayout.Width(120)))
            _ = RefreshLobbies();
        GUI.enabled = true;
        GUILayout.Label($"{_lobbies.Count} session(s)");
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Host", GUILayout.Width(220));
        GUILayout.Label("Players", GUILayout.Width(70));
        GUILayout.Label("Code", GUILayout.Width(80));
        GUILayout.Label("Lobby ID", GUILayout.Width(170));
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(340));
        foreach (var lobby in _lobbies)
        {
            GUILayout.BeginHorizontal();
            string host = lobby.GetData("host");
            GUILayout.Label(string.IsNullOrEmpty(host) ? "(unknown)" : host, GUILayout.Width(220));
            GUILayout.Label(DevSpectatorConfig.FormatBrowserPlayerCount(lobby), GUILayout.Width(70));
            GUILayout.Label(lobby.GetData("join_code"), GUILayout.Width(80));
            GUILayout.Label(lobby.Id.Value.ToString(), GUILayout.Width(170));

            GUI.enabled = !_isJoining;
            if (GUILayout.Button("Spectate", GUILayout.Width(90)))
                _ = SpectateLobby(lobby);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        if (_lobbies.Count == 0)
            GUILayout.Label(_isRefreshing ? "..." : "No sessions listed. Press Refresh.");
        GUILayout.EndScrollView();
    }

    private void DrawSpectatingPanel()
    {
        var lobbyManager = LobbyManager.Instance;
        var spectate = SpectateManager.Instance;
        var nm = NetworkManager.Singleton;

        string host = lobbyManager != null ? lobbyManager.CurrentLobby.GetData("host") : "";
        GUILayout.Label($"Session: {host}  (lobby {(lobbyManager != null ? lobbyManager.CurrentLobby.Id.Value : 0)})");
        GUILayout.Label($"Connection: {(nm != null && nm.IsConnectedClient ? "connected (hidden)" : "connecting...")}");

        if (lobbyManager != null)
        {
            GUILayout.Label("Players:");
            foreach (var member in lobbyManager.GetMembersSnapshot())
                GUILayout.Label($"  • {member.Name}");
        }

        GUILayout.Space(6);
        if (spectate != null)
        {
            var target = spectate.CurrentTarget;
            GUILayout.Label(target != null
                ? $"Watching: {target.name} (client {target.OwnerClientId})  —  {spectate.SpectatableCount} spectatable"
                : "Watching: nobody (no living, spawned players yet)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Prev", GUILayout.Width(100))) spectate.SpectatePrevious();
            if (GUILayout.Button("Next >", GUILayout.Width(100))) spectate.SpectateNext();
            GUILayout.EndHorizontal();
        }

        _hideHud = GUILayout.Toggle(_hideHud, "Hide HUD");

        GUILayout.Space(6);
        if (GUILayout.Button("Leave session", GUILayout.Width(160)))
            _ = LeaveSpectating();
    }

    private void DrawFooter()
    {
        GUILayout.FlexibleSpace();
        if (!string.IsNullOrEmpty(_status))
            GUILayout.Label(_status);
        if (GUILayout.Button("Close", GUILayout.Width(100)))
            SetOpen(false);
        GUI.DragWindow();
    }
}
