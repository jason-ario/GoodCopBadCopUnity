using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 scripted task: the interrogation booth is left in a mess after the previous shift.
/// When triggered at the start of Day 3 the server activates the booth mess scene object,
/// network-spawns one blood splatter per blood spawn point, and network-spawns one junk
/// item per junk spawn point. The task completes when all blood is scrubbed and all junk
/// has been bagged.
///
/// Scene setup:
///   - NetworkObject on this GameObject — register in the NetworkManager prefab list.
///   - <see cref="_bloodSplatterPrefabs"/>: one or more registered Network Prefab blood splatters.
///   - <see cref="_bloodSpawnPoints"/>: Transform markers for blood splatter positions/scales.
///   - <see cref="_junkPrefabs"/>: one or more registered Network Prefab junk items (soldier parts).
///   - <see cref="_junkSpawnPoints"/>: Transform markers for junk item positions/scales.
///   - <see cref="_boothMessRoot"/>: the root In booth mess GameObject to activate on trigger.
///   - Call TriggerTask() from Day_03.DayActivated().
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanBoothMessTask : NetworkBehaviour, ISystemicThreat
{
    public static CleanBoothMessTask Instance { get; private set; }

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Task Properties")]
    [SerializeField] private string _taskName   = "Clean Booth Mess";
    [SerializeField] private float  _scoreWeight = 1f;

    [Header("Scene Objects")]
    [Tooltip("Root GameObject of the booth mess — activated when the task is triggered.")]
    [SerializeField] private GameObject _boothMessRoot;

    [Header("Blood Splatters")]
    [Tooltip("Pool of blood splatter prefabs to spawn. Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _bloodSplatterPrefabs;

    [Tooltip("Transform markers where blood splatters are spawned. Scale is copied to each spawned instance.")]
    [SerializeField] private Transform[] _bloodSpawnPoints;

    [Header("Junk Items")]
    [Tooltip("Pool of junk prefabs to spawn (e.g. soldier body parts). Each must be a registered Network Prefab.")]
    [SerializeField] private GameObject[] _junkPrefabs;

    [Tooltip("Transform markers where junk items are spawned. Scale is copied to each spawned instance.")]
    [SerializeField] private Transform[] _junkSpawnPoints;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _isActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _networkRemainingBlood = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _networkRemainingJunk = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Server-only state ────────────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedSplatters = new();
    private readonly List<NetworkObject> _spawnedJunk      = new();

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _taskName;
    public float  ScoreWeight => _scoreWeight;
    public float  ThreatLevel => _networkThreatLevel.Value;

    public string ThreatDescription =>
        (_networkRemainingBlood.Value + _networkRemainingJunk.Value) > 0
            ? $"Blood to scrub: {_networkRemainingBlood.Value}  |  Junk to bag: {_networkRemainingJunk.Value}"
            : "Booth cleaned!";

    /// <summary>Not used — this is a day-start task, not a night-phase threat.</summary>
    public void BeginNightPhase() { }

    /// <summary>Despawns any remaining objects when the night phase begins (day is over).</summary>
    public void EndNightPhase()
    {
        if (!IsServer) return;
        DespawnExistingSplatters();
        DespawnExistingJunk();
        _networkThreatLevel.Value       = 0f;
        _networkRemainingBlood.Value    = 0;
        _networkRemainingJunk.Value     = 0;
        _isActive.Value = false;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CleanBoothMessTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isActive.OnValueChanged += OnActiveChanged;

        // Handle the initial value for late-joining clients: if the booth cleanup was already
        // running before this client connected, register it in TaskRegistry and show the mess.
        ApplyActiveState(_isActive.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isActive.OnValueChanged -= OnActiveChanged;

        TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Day 3 activation ─────────────────────────────────────────────────────

    /// <summary>
    /// Activates the booth mess and spawns all blood splatters and junk items. SERVER ONLY.
    /// Call from Day_03.DayActivated().
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        if (_boothMessRoot != null)
            _boothMessRoot.SetActive(true);

        DespawnExistingSplatters();
        DespawnExistingJunk();
        SpawnAllSplatters();
        SpawnAllJunk();

        UpdateThreatLevel();

        // Set last: flipping _isActive is what registers the task in the HUD on every peer
        // (see OnActiveChanged), so the remaining-blood/junk counts must already be correct or the
        // freshly-added row would render "nothing left to clean" for a frame.
        _isActive.Value = true;

        Debug.Log($"[CleanBoothMessTask] Task triggered. {_networkRemainingBlood.Value} splatter(s), {_networkRemainingJunk.Value} junk item(s) spawned.");
    }

    // ── Callbacks ────────────────────────────────────────────────────────────

    /// <summary>Called on the server when one of the owned blood splatters is fully scrubbed.</summary>
    public void OnBloodScrubbed()
    {
        if (!IsServer) return;
        _networkRemainingBlood.Value = Mathf.Max(0, _networkRemainingBlood.Value - 1);
        UpdateThreatLevel();
        CheckCompletion();
    }

    /// <summary>Called on the server when one of the owned junk items is collected.</summary>
    public void OnJunkCollected()
    {
        if (!IsServer) return;
        _networkRemainingJunk.Value = Mathf.Max(0, _networkRemainingJunk.Value - 1);
        UpdateThreatLevel();
        CheckCompletion();
    }

    // ── Completion ────────────────────────────────────────────────────────────

    private void CheckCompletion()
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();

        if (_networkRemainingBlood.Value > 0 || _networkRemainingJunk.Value > 0) return;

        _networkThreatLevel.Value = 0f;

        // Unregisters on every peer via OnActiveChanged, including anyone who joins later.
        _isActive.Value = false;

        Debug.Log("[CleanBoothMessTask] Booth fully cleaned — task complete.");
    }

    // ── Spawning ─────────────────────────────────────────────────────────────

    private void SpawnAllSplatters()
    {
        _networkRemainingBlood.Value = 0;

        if (_bloodSplatterPrefabs == null || _bloodSplatterPrefabs.Length == 0 ||
            _bloodSpawnPoints == null || _bloodSpawnPoints.Length == 0)
        {
            Debug.LogWarning("[CleanBoothMessTask] No blood splatter prefabs or spawn points assigned.");
            return;
        }

        foreach (Transform point in _bloodSpawnPoints)
        {
            if (point == null) continue;

            GameObject prefab = _bloodSplatterPrefabs[Random.Range(0, _bloodSplatterPrefabs.Length)];
            GameObject go     = Instantiate(prefab, point.position, point.rotation);
            go.transform.localScale = point.localScale;

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[CleanBoothMessTask] Blood prefab '{prefab.name}' has no NetworkObject.");
                Destroy(go);
                continue;
            }

            GraffitiInteractable interactable = go.GetComponent<GraffitiInteractable>();
            if (interactable != null)
                interactable.OnScrubCompleted += OnBloodScrubbed;

            BloodTextureRandomizer randomizer = go.GetComponent<BloodTextureRandomizer>();
            if (randomizer != null)
            {
                randomizer.enabled = true;
                randomizer.Randomize();
            }

            netObj.Spawn(destroyWithScene: true);
            _spawnedSplatters.Add(netObj);
            _networkRemainingBlood.Value++;
        }
    }

    private void SpawnAllJunk()
    {
        _networkRemainingJunk.Value = 0;

        if (_junkPrefabs == null || _junkPrefabs.Length == 0 ||
            _junkSpawnPoints == null || _junkSpawnPoints.Length == 0)
        {
            Debug.LogWarning("[CleanBoothMessTask] No junk prefabs or spawn points assigned.");
            return;
        }

        for (int i = 0; i < _junkSpawnPoints.Length; i++)
        {
            Transform point = _junkSpawnPoints[i];
            if (point == null) continue;

            // Round-robin through prefabs so each type appears an equal number of times.
            GameObject prefab = _junkPrefabs[i % _junkPrefabs.Length];

            // Randomize Y rotation while preserving the spawn point's X/Z tilt.
            Vector3 euler = point.eulerAngles;
            Quaternion rotation = Quaternion.Euler(euler.x, Random.Range(0f, 360f), euler.z);

            GameObject go = Instantiate(prefab, point.position, rotation);
            go.transform.localScale = point.localScale;

            NetworkObject netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[CleanBoothMessTask] Junk prefab '{prefab.name}' has no NetworkObject.");
                Destroy(go);
                continue;
            }

            JunkItem junkItem = go.GetComponent<JunkItem>();
            if (junkItem != null)
                junkItem.OnCollected += OnJunkCollected;

            netObj.Spawn(destroyWithScene: true);
            _spawnedJunk.Add(netObj);
            _networkRemainingJunk.Value++;
        }
    }

    private void DespawnExistingSplatters()
    {
        foreach (NetworkObject netObj in _spawnedSplatters)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedSplatters.Clear();
        _networkRemainingBlood.Value = 0;
    }

    private void DespawnExistingJunk()
    {
        foreach (NetworkObject netObj in _spawnedJunk)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedJunk.Clear();
        _networkRemainingJunk.Value = 0;
    }

    // ── Threat level ──────────────────────────────────────────────────────────

    private void UpdateThreatLevel()
    {
        int total     = (_bloodSpawnPoints?.Length ?? 0) + (_junkSpawnPoints?.Length ?? 0);
        int remaining = _networkRemainingBlood.Value + _networkRemainingJunk.Value;
        _networkThreatLevel.Value = total > 0 ? (float)remaining / total : 0f;
    }

    // ── Client sync ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mirrors <see cref="_isActive"/> into the HUD task list and the booth mess visuals on every
    /// peer. Registration is driven purely by this replicated flag — deliberately NOT by a
    /// ClientRpc pair, which is what this task used to do and which silently skipped anyone who
    /// joined mid-cleanup: an RPC only reaches the clients connected at the instant it is sent, so
    /// a late joiner got no "clean the booth" row at all. Reading <see cref="_isActive"/> in
    /// <see cref="OnNetworkSpawn"/> plus reacting here covers both cases from one source of truth,
    /// matching every sibling task (TakeOutTrashTask, CleanBloodTask, FenceRepairTask, ...).
    /// </summary>
    private void OnActiveChanged(bool previous, bool current)
    {
        ApplyActiveState(current);
    }

    private void ApplyActiveState(bool active)
    {
        if (active)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);

        // The server drives _boothMessRoot directly in TriggerTask so it is already correct there;
        // remote clients follow the replicated flag. Applied in both directions so a client that
        // joins with the task inactive hides a booth mess left enabled in the authored scene.
        if (!IsServer && _boothMessRoot != null)
            _boothMessRoot.SetActive(active);
    }

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_bloodSpawnPoints != null)
        {
            Gizmos.color = new Color(0.8f, 0.1f, 0.1f, 0.9f);
            for (int i = 0; i < _bloodSpawnPoints.Length; i++)
            {
                if (_bloodSpawnPoints[i] == null) continue;
                Vector3 pos = _bloodSpawnPoints[i].position;
                Gizmos.DrawWireSphere(pos, 0.15f);
                Gizmos.DrawLine(pos, pos + _bloodSpawnPoints[i].forward * 0.3f);
                UnityEditor.Handles.Label(pos + Vector3.up * 0.25f, $"Blood {i}");
            }
        }

        if (_junkSpawnPoints != null)
        {
            Gizmos.color = new Color(0.9f, 0.6f, 0.1f, 0.9f);
            for (int i = 0; i < _junkSpawnPoints.Length; i++)
            {
                if (_junkSpawnPoints[i] == null) continue;
                Vector3 pos = _junkSpawnPoints[i].position;
                Gizmos.DrawWireSphere(pos, 0.2f);
                Gizmos.DrawLine(pos, pos + _junkSpawnPoints[i].forward * 0.4f);
                UnityEditor.Handles.Label(pos + Vector3.up * 0.35f, $"Junk {i}");
            }
        }
    }
#endif
}
