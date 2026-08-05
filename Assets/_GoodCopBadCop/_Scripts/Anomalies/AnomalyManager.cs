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
