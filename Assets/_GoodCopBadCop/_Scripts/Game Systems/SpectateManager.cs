using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpectateManager : MonoBehaviour
{
    public static SpectateManager Instance;

    [SerializeField] private SpectatorUI spectatorUI;

    private readonly List<PlayerInstance> _teammates = new List<PlayerInstance>();
    private bool _isSpectating = false;

    /// <summary>The teammate whose perspective is currently being watched.</summary>
    private PlayerInstance _currentTarget;

    private const float RetargetInterval = 0.5f;
    private float _retargetTimer;

    public bool IsSpectating => _isSpectating;
    public PlayerInstance CurrentTarget => _currentTarget;

    /// <summary>
    /// True while the watched teammate's AudioListener is the live one (dead local player
    /// spectating). The hidden dev spectator has no local PlayerInstance and uses its own rig listener.
    /// </summary>
    public bool IsAudioListenerOnTarget => _isSpectating
        && _currentTarget != null
        && _currentTarget.IsSpectatorListenerActive;

    /// <summary>Number of players that can currently be spectated.</summary>
    public int SpectatableCount
    {
        get
        {
            UpdateTeammateList();
            return _teammates.Count;
        }
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

    private void Update()
    {
        if (!_isSpectating) return;

        // Clicks inside the dev console must not cycle targets.
        if (!DevSessionBrowser.IsOpen)
        {
            if (Input.GetMouseButtonDown(0))
                SpectateNext();
            else if (Input.GetMouseButtonDown(1))
                SpectatePrevious();
        }

        // Automatically move on if the watched player died, despawned or disconnected.
        _retargetTimer -= Time.unscaledDeltaTime;
        if (_retargetTimer <= 0f)
        {
            _retargetTimer = RetargetInterval;
            if (!IsValidTarget(_currentTarget))
                SpectateNext();

            // Covers a target destroyed between ticks: hand audio back to the local player.
            RefreshLocalAudioListener();
        }
    }

    public void StartSpectating()
    {
        _isSpectating = true;
        _retargetTimer = RetargetInterval;

        if (spectatorUI != null)
            spectatorUI.gameObject.SetActive(true);

        SpectateNext();
    }

    public void SpectateNext() => Step(+1);

    public void SpectatePrevious() => Step(-1);

    private void Step(int direction)
    {
        UpdateTeammateList();

        if (_teammates.Count == 0)
        {
            ClearCurrentTarget();
            return;
        }

        int currentIndex = IsValidTarget(_currentTarget) ? _teammates.IndexOf(_currentTarget) : -1;
        int nextIndex = currentIndex < 0
            ? (direction > 0 ? 0 : _teammates.Count - 1)
            : ((currentIndex + direction) % _teammates.Count + _teammates.Count) % _teammates.Count;

        ApplySpectatorTarget(_teammates[nextIndex]);
    }

    private static bool IsValidTarget(PlayerInstance target)
    {
        return target != null
            && target != PlayerInstance.Instance
            && target.IsSpawned
            && target.PlayerHealth != null
            && !target.PlayerHealth.IsDead;
    }

    /// <summary>
    /// Switches all spectator-mode state to <paramref name="newTarget"/>, clearing the
    /// previous target's visual overrides first.
    /// </summary>
    private void ApplySpectatorTarget(PlayerInstance newTarget)
    {
        if (_currentTarget == newTarget) return;

        RestoreCurrentTarget();

        _currentTarget = newTarget;

        // Activate the new target's CinemachineCamera so the spectating client's
        // CinemachineBrain picks it up as the live camera (correct FOV, noise, etc.).
        _currentTarget.SetSpectatedByCamera(true);
        if (_currentTarget.PlayerAnimationController != null)
            _currentTarget.PlayerAnimationController.SetSpectatorMode(true);

        // A dead local player hears the world from the watched teammate's position.
        if (PlayerInstance.Instance != null)
            _currentTarget.SetSpectatorAudioListener(true);
        RefreshLocalAudioListener();

        Debug.Log($"[SpectateManager] Now spectating {_currentTarget.name}.");
    }

    private void RestoreCurrentTarget()
    {
        // Unity-null check (not ?.) so a destroyed target is skipped safely.
        if (_currentTarget == null) return;

        _currentTarget.SetSpectatorAudioListener(false);
        if (_currentTarget.PlayerAnimationController != null)
            _currentTarget.PlayerAnimationController.SetSpectatorMode(false);
        _currentTarget.SetSpectatedByCamera(false);
    }

    /// <summary>
    /// Keeps exactly one listener live for a local player: their own camera's, unless the
    /// watched teammate's listener has taken over. After a revive, PlayerInstance.Instance is
    /// already the replacement player, so this hands audio to the newly spawned player.
    /// </summary>
    private void RefreshLocalAudioListener()
    {
        var local = PlayerInstance.Instance;
        if (local == null) return;

        local.SetOwnAudioListenerEnabled(!IsAudioListenerOnTarget);
    }

    /// <summary>Clears spectator-mode visuals and resets the tracked target.</summary>
    private void ClearCurrentTarget()
    {
        if (ReferenceEquals(_currentTarget, null)) return;

        RestoreCurrentTarget();
        _currentTarget = null;
        RefreshLocalAudioListener();
        Debug.Log("[SpectateManager] No spectatable teammates available.");
    }

    /// <summary>Stops spectating and cleans up visual overrides.</summary>
    public void StopSpectating()
    {
        _isSpectating = false;
        ClearCurrentTarget();
        RefreshLocalAudioListener();

        if (spectatorUI != null)
            spectatorUI.Hide();
    }

    private void UpdateTeammateList()
    {
        _teammates.Clear();

        var networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            foreach (var client in networkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;

                var player = client.PlayerObject.GetComponent<PlayerInstance>();
                if (IsValidTarget(player) && !_teammates.Contains(player))
                    _teammates.Add(player);
            }
        }

        // Fallback for clients whose ConnectedClients view is incomplete (e.g. a hidden dev
        // spectator with no player object of its own): scan spawned player objects directly.
        if (_teammates.Count == 0)
        {
            foreach (var player in FindObjectsByType<PlayerInstance>(FindObjectsSortMode.None))
            {
                if (player.NetworkObject != null && player.NetworkObject.IsPlayerObject && IsValidTarget(player))
                    _teammates.Add(player);
            }
        }

        _teammates.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
    }
}
