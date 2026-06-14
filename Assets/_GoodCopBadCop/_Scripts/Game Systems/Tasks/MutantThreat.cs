using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: tracks active mutant presence and drops MutantBits on kill.
/// Replaces GoHuntingTask — no discrete completion goal. Threat level scales with
/// how many mutants are alive relative to <see cref="_maxThreatEnemies"/>.
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Assign _mutantSpawner.
///   - Assign _mutantBitPrefab (registered in NetworkManager's prefab list).
///   - Register this component in BetweenShiftTaskManager._threatBehaviours.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MutantThreat : NetworkBehaviour, ISystemicThreat
{
    public static MutantThreat Instance { get; private set; }

    /// <summary>
    /// True while the night phase is active.
    /// MutantEnemy reads this to decide whether to drop a bit on death.
    /// </summary>
    public static bool DropActive { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Mutant Activity";
    [SerializeField] private float _scoreWeight = 1f;

    [Header("Spawner Reference")]
    [Tooltip("The MutantSpawner that drives enemy population during the night phase.")]
    [SerializeField] private MutantSpawner _mutantSpawner;

    [Tooltip("Active-enemy count at which ThreatLevel reaches 1.")]
    [SerializeField] private int _maxThreatEnemies = 10;

    [Header("Bit Drop")]
    [Tooltip("MutantBit prefab. Must be registered as a Network Prefab.")]
    [SerializeField] private GameObject _mutantBitPrefab;

    [Range(0f, 1f)]
    [Tooltip("Probability (0–1) that a killed mutant drops a MutantBit while the night phase is active.")]
    [SerializeField] private float _dropChance = 0.5f;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedBits = new();
    private Coroutine _syncCoroutine;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription =>
        $"Active mutants: {(_mutantSpawner != null ? _mutantSpawner.ActiveEnemyCount : 0)}/{_maxThreatEnemies}";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MutantThreat] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStart;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStart;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance   = null;
            DropActive = false;
        }
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>Activates bit drops and starts syncing threat level from enemy count. SERVER ONLY.</summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        DropActive = true;
        DespawnExistingBits();

        if (_syncCoroutine != null) StopCoroutine(_syncCoroutine);
        _syncCoroutine = StartCoroutine(SyncThreatLevel());
    }

    /// <summary>Deactivates bit drops and stops syncing. SERVER ONLY.</summary>
    public void EndNightPhase()
    {
        if (!IsServer) return;

        DropActive = false;

        if (_syncCoroutine != null)
        {
            StopCoroutine(_syncCoroutine);
            _syncCoroutine = null;
        }
    }

    // ── Bit drop ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to spawn a MutantBit at <paramref name="position"/> based on the drop chance.
    /// Called by MutantEnemy.Die() on the server when a mutant is killed and DropActive is true.
    /// </summary>
    public void TryDropBitAt(Vector3 position)
    {
        if (!IsServer || !DropActive) return;

        if (_mutantBitPrefab == null)
        {
            Debug.LogError("[MutantThreat] _mutantBitPrefab is not assigned.");
            return;
        }

        if (Random.value > _dropChance) return;

        Vector3    spawnPos = position + Vector3.up * 0.3f;
        Quaternion spawnRot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject   bitGo  = Instantiate(_mutantBitPrefab, spawnPos, spawnRot);
        NetworkObject netObj = bitGo.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[MutantThreat] _mutantBitPrefab has no NetworkObject component.");
            Destroy(bitGo);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        _spawnedBits.Add(netObj);
    }

    // ── Day start ─────────────────────────────────────────────────────────────

    private void OnDayStart()
    {
        DropActive = false;

        if (!IsServer) return;

        if (_syncCoroutine != null)
        {
            StopCoroutine(_syncCoroutine);
            _syncCoroutine = null;
        }

        DespawnExistingBits();
        _networkThreatLevel.Value = 0f;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerator SyncThreatLevel()
    {
        while (true)
        {
            yield return new WaitForSeconds(3f);

            int activeCount = _mutantSpawner != null ? _mutantSpawner.ActiveEnemyCount : 0;
            _networkThreatLevel.Value = _maxThreatEnemies > 0
                ? Mathf.Clamp01((float)activeCount / _maxThreatEnemies)
                : 0f;
        }
    }

    private void DespawnExistingBits()
    {
        foreach (NetworkObject netObj in _spawnedBits)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedBits.Clear();
    }
}
