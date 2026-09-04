using System.Collections;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Systemic threat: perimeter fence segments periodically take damage during the night.
/// Fences are damaged continuously on a day-intensity scaled timer rather than as a one-time
/// batch at phase start.
///
/// Activates after <see cref="_firstActiveDay"/>. Fence damage persists into the day shift as
/// a tangible consequence of poor management the previous night.
///
/// Players reduce threat by repairing fences with a HammerPickable (via PerimiterFence).
/// Threat level equals damaged fence count divided by total fence count.
///
/// Like <see cref="FenceRepairTask"/>, the damaged count is <em>recounted</em> from live fence
/// state on every authoritative change rather than incremented/decremented — an incremental
/// tally drifts permanently out of true the first time an event is missed or a fence changes
/// state through a path the threat isn't watching. Both the count and the threat level are
/// replicated so the host and every client report the same numbers.
///
/// Scene setup:
///   - NetworkObject on this GameObject.
///   - Assign _allFences with every PerimiterFence instance in the scene.
///   - Register this component in BetweenShiftTaskManager._threatBehaviours.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class FenceThreat : NetworkBehaviour, ISystemicThreat
{
    public static FenceThreat Instance { get; private set; }

    [Header("Threat Properties")]
    [SerializeField] private string _threatName = "Perimeter Fence";
    [SerializeField] private float _scoreWeight = 0.5f;

    [Header("Fence Configuration")]
    [Tooltip("Every PerimiterFence in the scene.")]
    [SerializeField] private PerimiterFence[] _allFences;

    [Tooltip("Campaign day on which fence damage begins.")]
    [SerializeField] private int _firstActiveDay = 10;

    [Tooltip("Campaign day at which the damage rate reaches its peak values.")]
    [SerializeField] private int _peakScalingDay = 20;

    [Tooltip("Intensity curve: X = normalised day progress (0–1), Y = intensity (0–1).")]
    [SerializeField] private AnimationCurve _dayIntensityCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Damage Interval (Peak)")]
    [Tooltip("Minimum seconds between fence damage events at peak intensity.")]
    [SerializeField] private float _damageIntervalMin = 60f;

    [Tooltip("Maximum seconds between fence damage events at peak intensity.")]
    [SerializeField] private float _damageIntervalMax = 120f;

    [Header("Damage Interval (Sparse)")]
    [Tooltip("Minimum seconds between damage events at sparse (first-active-day) intensity.")]
    [SerializeField] private float _sparseDamageIntervalMin = 300f;

    [Tooltip("Maximum seconds between damage events at sparse intensity.")]
    [SerializeField] private float _sparseDamageIntervalMax = 600f;

    [Tooltip("Inclusive range for the damage level assigned to each fence.")]
    [SerializeField] private Vector2Int _damageRange = new Vector2Int(1, 3);

    // ── Networked state ──────────────────────────────────────────────────────

    private readonly NetworkVariable<float> _networkThreatLevel = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Replicated damaged-segment count. Previously a plain server-side int, which meant every
    /// client's guidebook row read "Damaged segments: 0/n" regardless of the real state.
    /// </summary>
    private readonly NetworkVariable<int> _damagedFenceCount = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Local state ──────────────────────────────────────────────────────────

    private Coroutine _damageCoroutine;
    private bool _observing;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription =>
        $"Damaged segments: {_damagedFenceCount.Value}/{(_allFences != null ? _allFences.Length : 0)}";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[FenceThreat] Duplicate instance detected — destroying self.");
            Destroy(this);
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Watch every fence for the whole session so the replicated count is correct even outside
        // the night phase (mutant breaches damage fences during the day too).
        if (IsServer)
        {
            StartObservingFences();
            Recount();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopObservingFences();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>
    /// Re-syncs damage tracking to whatever state the fences are actually in (no forced heal —
    /// fences only ever change health via hammer repair or mutant damage), then starts periodic
    /// damage if the current day meets the threshold. SERVER ONLY.
    /// </summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        StartObservingFences();
        Recount();

        int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;

        if (currentDay >= _firstActiveDay)
        {
            if (_damageCoroutine != null) StopCoroutine(_damageCoroutine);
            _damageCoroutine = StartCoroutine(DamageLoop());
        }
        else
        {
            Debug.Log($"[FenceThreat] Day {currentDay} is below first-active day ({_firstActiveDay}) — skipping damage loop.");
        }
    }

    /// <summary>
    /// Stops the damage loop. Existing fence damage persists into the day shift. SERVER ONLY.
    /// </summary>
    public void EndNightPhase()
    {
        if (!IsServer) return;

        if (_damageCoroutine != null)
        {
            StopCoroutine(_damageCoroutine);
            _damageCoroutine = null;
        }
    }

    // ── Fence observation ─────────────────────────────────────────────────────

    private void StartObservingFences()
    {
        if (_observing || _allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null)
                fence.OnDamageStateChangedServer += HandleFenceStateChanged;
        }

        _observing = true;
    }

    private void StopObservingFences()
    {
        if (!_observing || _allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null)
                fence.OnDamageStateChangedServer -= HandleFenceStateChanged;
        }

        _observing = false;
    }

    private void HandleFenceStateChanged(PerimiterFence fence) => Recount();

    /// <summary>Recounts visibly damaged segments from live fence state. Server-only.</summary>
    private void Recount()
    {
        if (!IsServer) return;

        int total   = _allFences != null ? _allFences.Length : 0;
        int damaged = 0;

        if (_allFences != null)
        {
            foreach (PerimiterFence fence in _allFences)
            {
                if (fence != null && fence.IsBroken) damaged++;
            }
        }

        _damagedFenceCount.Value  = damaged;
        _networkThreatLevel.Value = total > 0 ? Mathf.Clamp01((float)damaged / total) : 0f;
    }

    // ── Damage loop ───────────────────────────────────────────────────────────

    private IEnumerator DamageLoop()
    {
        while (true)
        {
            float intensity   = GetDayIntensity();
            float intervalMin = Mathf.Lerp(_sparseDamageIntervalMin, _damageIntervalMin, intensity);
            float intervalMax = Mathf.Lerp(_sparseDamageIntervalMax, _damageIntervalMax, intensity);
            float interval    = Random.Range(intervalMin, intervalMax);

            yield return new WaitForSeconds(interval);

            // Re-check day threshold each iteration in case the day changes mid-night.
            int currentDay = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
            if (currentDay < _firstActiveDay) continue;

            DamageSingleFence();
        }
    }

    private void DamageSingleFence()
    {
        if (_allFences == null || _allFences.Length == 0) return;

        // Collect intact fences.
        var candidates = new System.Collections.Generic.List<PerimiterFence>();
        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null && fence.IsSpawned && !fence.IsBroken)
                candidates.Add(fence);
        }

        if (candidates.Count == 0) return;

        PerimiterFence target = candidates[Random.Range(0, candidates.Count)];

        int maxAllowed  = target.MaxDamageLevel;
        int damageMin   = Mathf.Clamp(_damageRange.x, 1, Mathf.Max(1, maxAllowed));
        int damageMax   = Mathf.Clamp(_damageRange.y, damageMin, Mathf.Max(1, maxAllowed));
        int damageLevel = Random.Range(damageMin, damageMax + 1);

        // Raise damage only — never heal a segment mutants already hit harder.
        target.EnsureMinimumDamageLevelServer(damageLevel);

        // Recount runs via HandleFenceStateChanged, but call it explicitly in case the level was
        // already at or below the fence's current damage (no state change fired).
        Recount();

        Debug.Log($"[FenceThreat] Fence damaged at level {damageLevel}. Damaged: {_damagedFenceCount.Value}/{_allFences.Length}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float GetDayIntensity()
    {
        if (CampaignManager.Instance == null) return 1f;

        int day   = CampaignManager.Instance.CurrentDay;
        int range = _peakScalingDay - _firstActiveDay;

        if (range <= 0) return 1f;

        float t = Mathf.Clamp01((float)(day - _firstActiveDay) / range);
        return _dayIntensityCurve.Evaluate(t);
    }
}
