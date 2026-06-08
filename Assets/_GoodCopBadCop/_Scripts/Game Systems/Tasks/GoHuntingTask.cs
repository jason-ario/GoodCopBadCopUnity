using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Between-shift task: kill mutants to collect MutantBits, then deposit 5 of them in the PostBox.
///
/// Mutants have a configurable chance (default 50%) to drop a MutantBit on death while this task
/// is active. Bits can be picked up and carried to the PostBox like any PickableObject.
///
/// Scene setup:
///   - Add a NetworkObject component to this GameObject.
///   - Assign _mutantBitPrefab (must be registered as a Network Prefab in NetworkManager).
///   - Register this component on BetweenShiftTaskManager via the Inspector task list.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class GoHuntingTask : NetworkBehaviour, IBetweenShiftTask
{
    public static GoHuntingTask Instance { get; private set; }

    /// <summary>
    /// True while the task is currently active (between shifts).
    /// Mutants read this flag to decide whether to attempt a bit drop on death.
    /// </summary>
    public static bool IsTaskActive { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Go Hunting";
    [SerializeField] private int _couponReward = 10;
    [SerializeField] private int _totalBits = 5;

    [Header("Spawning")]
    [Tooltip("MutantBit prefab to drop on mutant death. Must be registered as a Network Prefab.")]
    [SerializeField] private GameObject _mutantBitPrefab;

    [Range(0f, 1f)]
    [Tooltip("Probability (0–1) that a killed mutant drops a MutantBit while the task is active.")]
    [SerializeField] private float _dropChance = 0.5f;

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    public string TaskName => _taskName;
    public int CouponReward => _couponReward;
    public bool IsComplete => _isComplete;

    /// <summary>Dynamic description reflects current deposit progress.</summary>
    public string TaskDescription =>
        _isComplete
            ? $"All {_totalBits} bits deposited!"
            : $"Deposit mutant bits: {_bitsDeposited.Value}/{_totalBits}";

    // ── Networked state ──────────────────────────────────────────────────────

    private NetworkVariable<int> _bitsDeposited = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Local flag — set on all clients via MarkCompleteClientRpc.
    private bool _isComplete;

    // Server-side: tracks spawned bits so they can be cleaned up on reset.
    private readonly List<NetworkObject> _spawnedBits = new();

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GoHuntingTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _bitsDeposited.OnValueChanged += OnBitsDepositedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _bitsDeposited.OnValueChanged -= OnBitsDepositedChanged;

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            IsTaskActive = false;
        }
    }

    // ── IBetweenShiftTask ────────────────────────────────────────────────────

    /// <summary>
    /// Resets task state at the start of each night phase.
    /// Called on every client by BetweenShiftTaskManager.
    /// Bit cleanup is server-only.
    /// </summary>
    public void ResetTask()
    {
        _isComplete = false;
        IsTaskActive = true;

        if (!IsServer) return;

        _bitsDeposited.Value = 0;
        DespawnExistingBits();
    }

    // ── Drop logic ────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to spawn a MutantBit at <paramref name="position"/> based on the drop chance.
    /// Called from MutantEnemy on the server when a mutant dies and the task is active.
    /// </summary>
    public void TryDropBitAt(Vector3 position)
    {
        if (!IsServer || !IsTaskActive || _isComplete) return;

        if (_mutantBitPrefab == null)
        {
            Debug.LogError("[GoHuntingTask] _mutantBitPrefab is not assigned.");
            return;
        }

        if (Random.value > _dropChance) return;

        Vector3 spawnPos = position + Vector3.up * 0.3f;
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject bitGo = Instantiate(_mutantBitPrefab, spawnPos, spawnRot);
        NetworkObject netObj = bitGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[GoHuntingTask] MutantBit prefab has no NetworkObject component.");
            Destroy(bitGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedBits.Add(netObj);
    }

    // ── Deposit flow ─────────────────────────────────────────────────────────

    /// <summary>
    /// Called by PostBox on the local client after a MutantBit is accepted.
    /// Routes the deposit to the server, which is the single authority for the counter.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void DepositBitServerRpc()
    {
        if (_isComplete) return;

        _bitsDeposited.Value = Mathf.Clamp(_bitsDeposited.Value + 1, 0, _totalBits);

        if (_bitsDeposited.Value >= _totalBits)
        {
            _isComplete = true;
            IsTaskActive = false;

            if (BetweenShiftTaskManager.Instance != null)
                BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);

            MarkCompleteClientRpc();
        }
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        IsTaskActive = false;
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    /// <summary>Deactivates the task when a new day begins so mutants stop dropping bits.</summary>
    private void OnDayStart()
    {
        IsTaskActive = false;
        _isComplete = false;

        if (IsServer)
            _bitsDeposited.Value = 0;
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    private void DespawnExistingBits()
    {
        foreach (NetworkObject netObj in _spawnedBits)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedBits.Clear();
    }

    // ── Progress sync ──────────────────────────────────────────────────────────

    private void OnBitsDepositedChanged(int previous, int current)
    {
        GuidebookTaskRegistry.Instance.NotifyTaskStateChanged();
    }
}
