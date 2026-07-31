using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Day 3 scripted task: mop up every blood splatter left behind by the gore/body-part junk
/// <see cref="TakeOutTrashTask"/> scatters across the yard. Operates exactly like
/// <see cref="CleanGraffitiTask"/> — a <see cref="GraffitiInteractable"/> scrubbed clean with a
/// <see cref="Mop"/> — except this task doesn't spawn its own splatters or use fixed spawn
/// points. <see cref="TakeOutTrashTask"/> spawns one blood decal per gore piece at a random
/// yard position and hands each one to <see cref="RegisterBloodSplatter"/> as it's created, so
/// this task's total grows to match however much gore actually landed that cycle.
///
/// Implements <see cref="ISystemicThreat"/> for HUD / performance scoring, same as
/// <see cref="CleanGraffitiTask"/> and <see cref="TakeOutTrashTask"/>.
///
/// Scene setup:
///   - NetworkObject on this GameObject (in-scene placed — no prefab registration needed).
///   - No prefabs or spawn points to assign here — TakeOutTrashTask feeds this task splatters
///     directly via RegisterBloodSplatter.
///   - The blood decal prefab(s) assigned to TakeOutTrashTask's _bloodDecalPrefabs must each
///     have a GraffitiInteractable component (e.g. "Random Blood Splatter Variant.prefab") —
///     decals without one are skipped (treated as purely cosmetic, not counted).
///   - Call TriggerTask() from Day_03.DayActivated() BEFORE TakeOutTrashTask.TriggerTask(useGorePrefabs: true)
///     so the task is active in time to register every blood decal spawned that cycle.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class CleanBloodTask : NetworkBehaviour, ISystemicThreat
{
    public static CleanBloodTask Instance { get; private set; }

    [Header("Task Properties")]
    [SerializeField] private string _taskName = "Clean Blood";
    [Tooltip("Number of coupons the ATM dispenses when all blood has been scrubbed.")]
    [SerializeField] private int _couponReward = 10;

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<int> _scrubbed = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Total blood splatters registered for this task run.</summary>
    private readonly NetworkVariable<int> _totalCount = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether this task is currently active and should appear in the HUD task list.
    /// Drives TaskRegistry registration on all clients, including late joiners.
    /// </summary>
    private readonly NetworkVariable<bool> _isActive = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Local state (server-only) ─────────────────────────────────────────────

    private readonly List<NetworkObject> _spawnedSplatters = new();
    private bool _taskActive;
    private bool _isComplete;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName  => _taskName;
    public float  ScoreWeight => 1f;

    public float ThreatLevel => _totalCount.Value > 0
        ? 1f - Mathf.Clamp01((float)_scrubbed.Value / _totalCount.Value)
        : 0f;

    public string ThreatDescription =>
        _isComplete
            ? $"All {_totalCount.Value} splatter(s) scrubbed!"
            : _totalCount.Value > 0
                ? $"Scrub blood: {_scrubbed.Value}/{_totalCount.Value}"
                : string.Empty;

    /// <summary>No-op — this task is triggered explicitly on Day 3, not by the night phase.</summary>
    public void BeginNightPhase() { }

    /// <summary>No-op.</summary>
    public void EndNightPhase() { }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CleanBloodTask] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _scrubbed.OnValueChanged   += OnProgressChanged;
        _totalCount.OnValueChanged += OnProgressChanged;
        _isActive.OnValueChanged   += OnIsActiveChanged;

        // Handle the initial value for late-joining clients.
        if (_isActive.Value)
            TaskRegistry.Instance?.AddThreat(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _scrubbed.OnValueChanged   -= OnProgressChanged;
        _totalCount.OnValueChanged -= OnProgressChanged;
        _isActive.OnValueChanged   -= OnIsActiveChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Activates the blood-cleanup task fresh for this cycle: despawns any leftover splatters
    /// from a previous trigger, resets progress to zero, and marks the task active so
    /// subsequent <see cref="RegisterBloodSplatter"/> calls count toward the total. Server-only.
    /// Call this BEFORE spawning the gore/blood for the cycle (see class doc comment).
    /// </summary>
    public void TriggerTask()
    {
        if (!IsServer) return;

        DespawnExistingSplatters();

        _taskActive  = true;
        _isComplete  = false;
        _scrubbed.Value    = 0;
        _totalCount.Value  = 0;

        _isActive.Value = true;

        // Explicitly re-register on every client rather than relying solely on
        // _isActive's OnValueChanged — if the task was already active this cycle, that
        // NetworkVariable write is a no-op and OnIsActiveChanged never fires, silently
        // dropping the task from the HUD (see the equivalent fix in TakeOutTrashTask).
        RegisterInTaskRegistryClientRpc();

        Debug.Log("[CleanBloodTask] Task triggered — awaiting blood splatter registrations.");
    }

    /// <summary>
    /// Registers an externally-spawned blood splatter's NetworkObject with this task — called
    /// by <see cref="TakeOutTrashTask"/> immediately after it spawns a blood decal alongside a
    /// gore piece. Routes the splatter's <see cref="GraffitiInteractable.OnScrubCompleted"/>
    /// callback to this task and counts it toward the total. No-op if the task isn't active or
    /// the splatter has no <see cref="GraffitiInteractable"/> (a purely cosmetic decal variant
    /// with nothing to mop). Server-only.
    /// </summary>
    public void RegisterBloodSplatter(NetworkObject netObj)
    {
        if (!IsServer || netObj == null || !_taskActive) return;

        GraffitiInteractable interactable = netObj.GetComponent<GraffitiInteractable>();
        if (interactable == null) return;

        interactable.OnScrubCompleted = OnBloodScrubbed;

        _spawnedSplatters.Add(netObj);
        _totalCount.Value++;
    }

    // ── Scrub callback (called by GraffitiInteractable on the server) ─────────

    /// <summary>
    /// Called by a registered splatter's <see cref="GraffitiInteractable"/> on the server once
    /// it has been fully scrubbed. Increments progress and completes the task once every
    /// splatter registered this cycle has been cleaned.
    /// </summary>
    private void OnBloodScrubbed()
    {
        if (!IsServer || _isComplete) return;

        _scrubbed.Value = Mathf.Clamp(_scrubbed.Value + 1, 0, _totalCount.Value);

        if (_scrubbed.Value < _totalCount.Value) return;

        _isComplete = true;
        _taskActive = false;

        ATM.Instance?.SpawnCoupons(_couponReward);

        MarkCompleteClientRpc();

        // Hide from HUD once all splatters are clean.
        _isActive.Value = false;

        Debug.Log("[CleanBloodTask] All blood splatters scrubbed — task complete.");
    }

    [ClientRpc]
    private void MarkCompleteClientRpc()
    {
        _isComplete = true;
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }

    private void DespawnExistingSplatters()
    {
        foreach (NetworkObject netObj in _spawnedSplatters)
        {
            if (netObj != null && netObj.IsSpawned)
                netObj.Despawn(destroy: true);
        }
        _spawnedSplatters.Clear();
    }

    // ── Registry management ───────────────────────────────────────────────────

    [ClientRpc]
    private void RegisterInTaskRegistryClientRpc()
    {
        TaskRegistry.Instance?.AddThreat(this);
    }

    private void OnIsActiveChanged(bool previous, bool current)
    {
        if (current)
            TaskRegistry.Instance?.AddThreat(this);
        else
            TaskRegistry.Instance?.RemoveThreat(this);
    }

    private void OnProgressChanged(int previous, int current)
    {
        TaskRegistry.Instance?.NotifyTaskStateChanged();
    }
}
