using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Systemic threat: perimeter fence segments periodically take damage during the night.
/// Replaces FenceRepairTask — fences are damaged continuously on a day-intensity scaled timer
/// rather than as a one-time batch at phase start.
///
/// Activates after <see cref="_firstActiveDay"/>. Fence damage persists into the day shift as
/// a tangible consequence of poor management the previous night.
///
/// Players reduce threat by repairing fences with a HammerPickable (via PerimiterFence).
/// Threat level equals damaged fence count divided by total fence count.
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

    // ── Local state ──────────────────────────────────────────────────────────

    private int _damagedFenceCount;
    private readonly List<PerimiterFence> _damagedFences = new();
    private Coroutine _damageCoroutine;

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    public string ThreatName        => _threatName;
    public float  ScoreWeight       => _scoreWeight;
    public float  ThreatLevel       => _networkThreatLevel.Value;

    public string ThreatDescription =>
        $"Damaged segments: {_damagedFenceCount}/{(_allFences != null ? _allFences.Length : 0)}";

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
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        UnsubscribeFromDamagedFences();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── ISystemicThreat ──────────────────────────────────────────────────────

    /// <summary>
    /// Heals all fences, then starts periodic damage if the current day meets the threshold.
    /// SERVER ONLY.
    /// </summary>
    public void BeginNightPhase()
    {
        if (!IsServer) return;

        UnsubscribeFromDamagedFences();
        HealAllFences();
        _damagedFences.Clear();
        _damagedFenceCount        = 0;
        _networkThreatLevel.Value = 0f;

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

    // ── Repair callback ───────────────────────────────────────────────────────

    private void HandleFenceRepaired(PerimiterFence fence)
    {
        Debug.Assert(IsServer, "[FenceThreat] HandleFenceRepaired called on non-server.");

        fence.OnFullyRepaired -= HandleFenceRepaired;
        _damagedFences.Remove(fence);
        _damagedFenceCount = Mathf.Max(0, _damagedFenceCount - 1);

        UpdateThreatLevel();

        Debug.Log($"[FenceThreat] Fence repaired. Damaged: {_damagedFenceCount}/{(_allFences?.Length ?? 0)}");
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

        // Collect undamaged fences.
        List<PerimiterFence> candidates = new();
        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null && !_damagedFences.Contains(fence))
                candidates.Add(fence);
        }

        if (candidates.Count == 0) return;

        PerimiterFence target = candidates[Random.Range(0, candidates.Count)];

        int maxAllowed  = target.MaxDamageLevel;
        int damageMin   = Mathf.Min(_damageRange.x, maxAllowed);
        int damageMax   = Mathf.Min(_damageRange.y, maxAllowed);
        int damageLevel = Random.Range(damageMin, damageMax + 1);

        target.SetDamageLevelServer(damageLevel);
        target.OnFullyRepaired += HandleFenceRepaired;

        _damagedFences.Add(target);
        _damagedFenceCount++;

        UpdateThreatLevel();

        Debug.Log($"[FenceThreat] Fence damaged at level {damageLevel}. Damaged: {_damagedFenceCount}/{(_allFences?.Length ?? 0)}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void HealAllFences()
    {
        if (_allFences == null) return;

        foreach (PerimiterFence fence in _allFences)
        {
            if (fence != null)
                fence.SetDamageLevelServer(0);
        }
    }

    private void UnsubscribeFromDamagedFences()
    {
        foreach (PerimiterFence fence in _damagedFences)
        {
            if (fence != null)
                fence.OnFullyRepaired -= HandleFenceRepaired;
        }
    }

    private void UpdateThreatLevel()
    {
        int total = _allFences != null ? _allFences.Length : 1;
        _networkThreatLevel.Value = total > 0
            ? Mathf.Clamp01((float)_damagedFenceCount / total)
            : 0f;
    }

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
