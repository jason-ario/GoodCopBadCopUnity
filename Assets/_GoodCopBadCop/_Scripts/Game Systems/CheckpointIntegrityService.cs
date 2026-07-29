using System;
using UnityEngine;

/// <summary>
/// Tracks how well the checkpoint booth is currently being maintained — graffiti coverage,
/// uncollected trash, and damaged perimeter fence segments — and converts that into a
/// persistent "Checkpoint Integrity Score". The score is a payout multiplier applied to every
/// coupon amount dispensed by the <see cref="ATM"/>: 100% when graffiti, trash, and fences are
/// all fully clean/repaired, dropping toward <see cref="_minScore"/> (50% by default) as more
/// of those three categories are left unattended.
///
/// This is intentionally scoped to exactly three systemic threats — <see cref="GraffitiThreat"/>,
/// <see cref="TakeOutTrashTask"/>, and <see cref="FenceThreat"/> — rather than every
/// <see cref="ISystemicThreat"/> in the game (e.g. mutants), since those three represent the
/// physical state of the booth itself. Each is weighted equally by default via
/// <see cref="_categoryWeights"/>.
///
/// Purely a read-side aggregator: it owns no networked state of its own. Every tracked
/// threat already replicates its ThreatLevel via NetworkVariable(ReadPermission.Everyone), so
/// every client — and the server, where <see cref="ATM.SpawnCoupons"/> actually runs — computes
/// the same score locally from those already-synced values.
///
/// Self-instantiates on first access, so no manual scene placement is required — mirrors the
/// pattern used by <see cref="TaskRegistry"/>.
/// </summary>
public class CheckpointIntegrityService : MonoBehaviour
{
    public static CheckpointIntegrityService Instance => GetOrCreate();

    [Header("Score Range")]
    [Tooltip("Payout multiplier when the booth is perfectly clean (graffiti, trash, and fences all at 0 pressure). 1 = 100% of the base payout.")]
    [SerializeField, Range(0f, 2f)] private float _maxScore = 1f;

    [Tooltip("Payout multiplier when every tracked category is at maximum pressure (fully messy). 0.5 = 50% of the base payout.")]
    [SerializeField, Range(0f, 1f)] private float _minScore = 0.5f;

    [Header("Category Weights")]
    [Tooltip("Relative weight of graffiti coverage in the aggregate score.")]
    [SerializeField] private float _graffitiWeight = 1f;

    [Tooltip("Relative weight of uncollected trash in the aggregate score.")]
    [SerializeField] private float _trashWeight = 1f;

    [Tooltip("Relative weight of damaged perimeter fence segments in the aggregate score.")]
    [SerializeField] private float _fenceWeight = 1f;

    [Header("Refresh")]
    [Tooltip("Seconds between automatic recalculations of the aggregate score.")]
    [SerializeField] private float _refreshInterval = 0.5f;

    /// <summary>Fired whenever the integrity score changes, with the new multiplier value (range: _minScore.._maxScore).</summary>
    public static event Action<float> OnIntegrityScoreChanged;

    /// <summary>
    /// Current payout multiplier and HUD percentage value. 1 = 100% (booth fully clean),
    /// down to <see cref="_minScore"/> (50% by default) when every tracked category is at
    /// maximum pressure. Multiply a base coupon amount by this value, or use
    /// <see cref="ApplyMultiplier"/>.
    /// </summary>
    public float IntegrityScore { get; private set; } = 1f;

    /// <summary>Upper bound of <see cref="IntegrityScore"/> — used by HUD bars to normalise the fill amount.</summary>
    public float MaxScore => _maxScore;

    private static CheckpointIntegrityService _instance;
    private float _timer;

    // ── Self-instantiation ───────────────────────────────────────────────────

    private static CheckpointIntegrityService GetOrCreate()
    {
        if (_instance != null) return _instance;

        _instance = FindFirstObjectByType<CheckpointIntegrityService>();
        if (_instance != null) return _instance;

        var go = new GameObject("[CheckpointIntegrityService]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CheckpointIntegrityService>();
        return _instance;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Recalculate();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < _refreshInterval) return;

        _timer = 0f;
        Recalculate();
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes <see cref="IntegrityScore"/> from the current threat levels and fires
    /// <see cref="OnIntegrityScoreChanged"/> if it changed. Safe to call from anywhere
    /// (e.g. immediately before a payout) to force an up-to-date read.
    /// </summary>
    public void Recalculate()
    {
        float cleanliness = GetWeightedCleanliness();
        float newScore = Mathf.Lerp(_minScore, _maxScore, cleanliness);

        if (Mathf.Approximately(newScore, IntegrityScore)) return;

        IntegrityScore = newScore;
        OnIntegrityScoreChanged?.Invoke(IntegrityScore);
    }

    /// <summary>
    /// Applies the current integrity multiplier to a base coupon amount, rounding to the
    /// nearest whole coupon (minimum 1). Recalculates first so the multiplier used always
    /// reflects the booth's state at the moment of payout.
    /// </summary>
    public int ApplyMultiplier(int baseAmount)
    {
        Recalculate();
        return Mathf.Max(1, Mathf.RoundToInt(baseAmount * IntegrityScore));
    }

    // ── Cleanliness sampling ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the weighted average cleanliness (0 = all tracked categories at max mess,
    /// 1 = all tracked categories perfectly clean) across graffiti, trash, and fences.
    /// Categories whose systemic threat isn't currently spawned in the scene are skipped
    /// rather than counted as clean or messy.
    /// </summary>
    private float GetWeightedCleanliness()
    {
        float weightedTotal = 0f;
        float totalWeight   = 0f;

        if (GraffitiThreat.Instance != null && _graffitiWeight > 0f)
        {
            weightedTotal += (1f - Mathf.Clamp01(GraffitiThreat.Instance.ThreatLevel)) * _graffitiWeight;
            totalWeight   += _graffitiWeight;
        }

        if (TakeOutTrashTask.Instance != null && _trashWeight > 0f)
        {
            weightedTotal += (1f - Mathf.Clamp01(TakeOutTrashTask.Instance.ThreatLevel)) * _trashWeight;
            totalWeight   += _trashWeight;
        }

        if (FenceThreat.Instance != null && _fenceWeight > 0f)
        {
            weightedTotal += (1f - Mathf.Clamp01(FenceThreat.Instance.ThreatLevel)) * _fenceWeight;
            totalWeight   += _fenceWeight;
        }

        return totalWeight > 0f ? weightedTotal / totalWeight : 1f;
    }
}
