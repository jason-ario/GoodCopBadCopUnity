using System;
using UnityEngine;

/// <summary>
/// Central place to configure how much each anomaly category "costs" toward a suspect's
/// mutation score budget. <see cref="AnomalyController"/> spends this budget to decide how
/// many anomalies of each category to activate for a given mutation score.
///
/// Mutation score reference scale (0–10, higher = more mutated):
/// <list type="bullet">
///   <item>10 — full mutant.</item>
///   <item>8 — will mutate that night.</item>
///   <item>5 — a fair quarantine amount.</item>
/// </list>
/// </summary>
public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;

    [Header("Mutation Score Anomaly Point Costs")]
    [Tooltip("Points spent from the suspect's mutation score budget for each active Physical (mutation) anomaly.")]
    [SerializeField] private int _physicalPoints = 2;

    [Tooltip("Points spent from the suspect's mutation score budget for each active Documentation anomaly.")]
    [SerializeField] private int _documentationPoints = 1;

    [Tooltip("Points spent from the suspect's mutation score budget for each active Behavior anomaly.")]
    [SerializeField] private int _behaviorPoints = 1;

    [Tooltip("Points spent from the suspect's mutation score budget for each active Supernatural anomaly.")]
    [SerializeField] private int _supernaturalPoints = 2;

    [Tooltip("Points spent from the suspect's mutation score budget for each active Vitals anomaly.")]
    [SerializeField] private int _vitalsPoints = 1;

    [Header("Global Tuning")]
    [Tooltip("Range a random multiplier is drawn from once per campaign day in SuspectRunRecords.AdvanceDayInfection(). " +
             "The same rolled value is applied to every suspect's daily infection increase that day, so some days " +
             "are calmer (near the low end) and some days spike (near the high end). Set both X and Y to 1 to disable " +
             "day-level randomness and always apply a flat 1x. Set both to 0 to freeze infection progression entirely.")]
    [SerializeField] private Vector2 _infectionProgressionMultiplierRange = new Vector2(0.8f, 1.43f);

    /// <summary>
    /// Rolls a fresh random multiplier within <see cref="_infectionProgressionMultiplierRange"/>. Callers should
    /// roll this once per day (not once per suspect) so every suspect shares the same "how bad was today" swing.
    /// Never negative — a range of (0, 0) freezes infection progression entirely without touching per-suspect data.
    /// </summary>
    public float RollInfectionProgressionMultiplier()
    {
        float min = Mathf.Max(0f, Mathf.Min(_infectionProgressionMultiplierRange.x, _infectionProgressionMultiplierRange.y));
        float max = Mathf.Max(0f, Mathf.Max(_infectionProgressionMultiplierRange.x, _infectionProgressionMultiplierRange.y));
        return UnityEngine.Random.Range(min, max);
    }

    [Header("Never-Seen Suspect Tiered Starting Score")]
    [Tooltip("Starting mutation score granted to a suspect the very first time they're shown to the " +
             "player on campaign day 1 (i.e. no backlog days have elapsed). Later, never-seen-before " +
             "suspects get more than this — see NeverSeenScorePerDay below.")]
    [SerializeField] private int _neverSeenBaseStartingScore = 0;

    [Tooltip("Extra starting mutation score points added for every campaign day that has elapsed " +
             "before a suspect's first-ever appearance. E.g. with a value of 3, a suspect never seen " +
             "until day 4 starts with roughly BaseStartingScore + 3*(4-1) points instead of 0 — showcasing " +
             "more advanced physical mutations later in the run without making them as far along as a " +
             "suspect who has actually been seen and progressed day over day.")]
    [SerializeField] private int _neverSeenScorePerDay = 3;

    [Tooltip("Hard cap on the tiered starting score a never-seen-before suspect can receive, no matter " +
              "how late in the campaign their first appearance happens. Keeps late-game 'first sightings' " +
              "from starting as advanced as suspects who have been quarantined and let go multiple times.")]
    [SerializeField] private int _neverSeenMaxStartingScore = 40;

    /// <summary>
    /// Computes the tiered starting mutation score for a suspect who has never been shown to the
    /// player before, based on how many campaign days have already elapsed. Day 1 first-appearances
    /// get <see cref="_neverSeenBaseStartingScore"/>; each additional elapsed day adds
    /// <see cref="_neverSeenScorePerDay"/> more, up to <see cref="_neverSeenMaxStartingScore"/>.
    /// </summary>
    /// <param name="currentDay">The 1-based campaign day of the suspect's first appearance.</param>
    public int GetNeverSeenStartingScore(int currentDay)
    {
        int elapsedDays = Mathf.Max(0, currentDay - 1);
        int tieredScore = _neverSeenBaseStartingScore + (_neverSeenScorePerDay * elapsedDays);
        return Mathf.Clamp(tieredScore, _neverSeenBaseStartingScore, Mathf.Max(_neverSeenBaseStartingScore, _neverSeenMaxStartingScore));
    }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Returns the point cost of a single anomaly belonging to <paramref name="category"/>.
    /// Falls back to a cost of 1 for any category not explicitly configured.
    /// </summary>
    public int GetPointCost(AnomalyCategory category)
    {
        switch (category)
        {
            case AnomalyCategory.Documentation: return Mathf.Max(1, _documentationPoints);
            case AnomalyCategory.Vitals:        return Mathf.Max(1, _vitalsPoints);
            case AnomalyCategory.Behavior:      return Mathf.Max(1, _behaviorPoints);
            case AnomalyCategory.Mutations:     return Mathf.Max(1, _physicalPoints);
            case AnomalyCategory.Supernatural:  return Mathf.Max(1, _supernaturalPoints);
            default:                            return 1;
        }
    }
}
