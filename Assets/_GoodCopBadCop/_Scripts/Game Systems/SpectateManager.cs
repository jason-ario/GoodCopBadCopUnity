using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class SpectateManager : MonoBehaviour
{
    public static SpectateManager Instance;

    private List<PlayerInstance> _teammates = new List<PlayerInstance>();
    private int _currentIndex = 0;
    private bool _isSpectating = false;

    /// <summary>The teammate whose perspective is currently being watched.</summary>
    private PlayerInstance _currentTarget;

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

        if (Input.GetMouseButtonDown(0))
        {
            SpectateNext();
        }
    }

    public void StartSpectating()
    {
        _isSpectating = true;
        _currentIndex = -1;
        UpdateTeammateList();
        SpectateNext();
    }

    public void SpectateNext()
    {
        if (_teammates.Count == 0)
        {
            UpdateTeammateList();
            if (_teammates.Count == 0)
            {
                ClearCurrentTarget();
                return;
            }
        }

        _currentIndex = (_currentIndex + 1) % _teammates.Count;
        PlayerInstance target = _teammates[_currentIndex];

        // Guard against stale or self references — skip to the next valid entry.
        if (target == null || target == PlayerInstance.Instance)
        {
            if (_teammates.Count > 1)
                SpectateNext();
            return;
        }

        ApplySpectatorTarget(target);
    }

    /// <summary>
    /// Switches all spectator-mode state to <paramref name="newTarget"/>, clearing the
    /// previous target's visual overrides first.
    /// </summary>
    private void ApplySpectatorTarget(PlayerInstance newTarget)
    {
        if (_currentTarget == newTarget) return;

        // Restore previous target: deactivate their camera and clear visual overrides.
        _currentTarget?.PlayerAnimationController?.SetSpectatorMode(false);
        _currentTarget?.SetSpectatedByCamera(false);

        _currentTarget = newTarget;

        // Activate the new target's CinemachineCamera so the dead player's
        // CinemachineBrain picks it up as the live camera (correct FOV, noise, etc.).
        _currentTarget.SetSpectatedByCamera(true);
        _currentTarget.PlayerAnimationController?.SetSpectatorMode(true);

        Debug.Log($"[SpectateManager] Now spectating {_currentTarget.name}.");
    }

    /// <summary>Clears spectator-mode visuals and resets the tracked target.</summary>
    private void ClearCurrentTarget()
    {
        _currentTarget?.PlayerAnimationController?.SetSpectatorMode(false);
        _currentTarget?.SetSpectatedByCamera(false);
        _currentTarget = null;
        Debug.Log("[SpectateManager] No spectatable teammates available.");
    }

    /// <summary>Stops spectating and cleans up visual overrides.</summary>
    public void StopSpectating()
    {
        _isSpectating = false;
        ClearCurrentTarget();
    }

    private void UpdateTeammateList()
    {
        _teammates.Clear();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<PlayerInstance>();
                if (player != null && player != PlayerInstance.Instance && !player.PlayerHealth.IsDead)
                {
                    _teammates.Add(player);
                }
            }
        }
    }
}
