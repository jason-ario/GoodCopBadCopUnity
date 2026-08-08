using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Samples ISystemicThreat levels throughout the night phase and awards a performance-based
/// coupon bonus to all players at end of shift.
///
/// Sampling runs every <see cref="_sampleIntervalSeconds"/> seconds on the server. At shift end,
/// the weighted average "performance" (1 - average threat level) across all threats is mapped to
/// a coupon payout between <see cref="_minCouponReward"/> and <see cref="_maxCouponReward"/>.
///
/// Attach this MonoBehaviour to the same GameObject as BetweenShiftTaskManager.
/// </summary>
public class ShiftPerformanceEvaluator : MonoBehaviour
{
    [Tooltip("How often (seconds) threat levels are sampled during the night phase.")]
    [SerializeField] private float _sampleIntervalSeconds = 15f;

    [Tooltip("Minimum coupons awarded even if threats were at maximum pressure all night.")]
    [SerializeField] private int _minCouponReward = 2;

    [Tooltip("Maximum coupons awarded for a perfect (zero-threat) night.")]
    [SerializeField] private int _maxCouponReward = 10;

    /// <summary>Weighted average performance score from the last completed shift (0 = worst, 1 = best).</summary>
    public float LastShiftScore { get; private set; }

    private readonly Dictionary<ISystemicThreat, List<float>> _samples = new();
    private Coroutine _samplingCoroutine;

    private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Begins periodic sampling of the given threats. SERVER ONLY.
    /// Clears any samples from a previous shift before starting.
    /// </summary>
    public void BeginSampling(ISystemicThreat[] threats)
    {
        if (!IsServer) return;

        _samples.Clear();

        if (threats == null || threats.Length == 0) return;

        foreach (ISystemicThreat threat in threats)
        {
            if (threat != null)
                _samples[threat] = new List<float>();
        }

        if (_samplingCoroutine != null)
            StopCoroutine(_samplingCoroutine);

        _samplingCoroutine = StartCoroutine(SamplingLoop());
    }

    /// <summary>
    /// Stops sampling, calculates the weighted performance score, and awards coupons. SERVER ONLY.
    /// Stores the result in <see cref="LastShiftScore"/>.
    /// </summary>
    public void EvaluateAndAward()
    {
        if (!IsServer) return;

        if (_samplingCoroutine != null)
        {
            StopCoroutine(_samplingCoroutine);
            _samplingCoroutine = null;
        }

        if (_samples.Count == 0)
        {
            Debug.Log("[ShiftPerformanceEvaluator] No samples recorded — skipping evaluation.");
            return;
        }

        float totalWeight      = 0f;
        float weightedScore    = 0f;

        foreach (KeyValuePair<ISystemicThreat, List<float>> entry in _samples)
        {
            ISystemicThreat threat = entry.Key;
            List<float>     levels = entry.Value;

            if (levels.Count == 0 || threat.ScoreWeight <= 0f) continue;

            float avgThreat  = 0f;
            foreach (float l in levels) avgThreat += l;
            avgThreat /= levels.Count;

            float performance = 1f - avgThreat; // 0 = always at max threat, 1 = perfectly clean
            weightedScore += performance * threat.ScoreWeight;
            totalWeight   += threat.ScoreWeight;
        }

        LastShiftScore = totalWeight > 0f ? weightedScore / totalWeight : 0f;

        int coupons = Mathf.RoundToInt(Mathf.Lerp(_minCouponReward, _maxCouponReward, LastShiftScore));

        // Tasks/threat management no longer pays coupons — players are only paid for processing
        // suspects (see SuspectController.PayOutResults). Score is still tracked for logging.
        // if (ATM.Instance != null)
        //     ATM.Instance.SpawnCoupons(coupons);
        // else
        //     Debug.LogWarning("[ShiftPerformanceEvaluator] ATM.Instance is null — coupons not dispensed.");

        Debug.Log($"[ShiftPerformanceEvaluator] Shift score: {LastShiftScore:P0}. Performance bonus: {coupons} coupons.");
    }

    // ── Sampling loop ─────────────────────────────────────────────────────────

    private IEnumerator SamplingLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(_sampleIntervalSeconds);

            foreach (KeyValuePair<ISystemicThreat, List<float>> entry in _samples)
            {
                if (entry.Key != null)
                    entry.Value.Add(entry.Key.ThreatLevel);
            }
        }
    }
}
