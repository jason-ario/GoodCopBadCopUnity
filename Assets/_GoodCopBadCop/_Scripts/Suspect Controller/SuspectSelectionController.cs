using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SuspectSelectionController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private SuspectDatabase suspectDatabase;

    [Header("Daily Selection")]
    [SerializeField] private int suspectsPerDay = 6;

    [Header("Weight Tuning")]
    [SerializeField] private int baseWeight = 10;
    [SerializeField] private int unseenBonus = 8;
    [SerializeField] private int returningBonus = 6;
    [SerializeField] private int daysSinceSeenMultiplier = 2;
    [SerializeField] private int highInfectionBonus = 8;
    [SerializeField] private int criticalInfectionBonus = 15;
    [SerializeField] private int recentlySeenPenalty = 12;

    [Header("Pacing")]
    [SerializeField] private bool guaranteeAtLeastOneHighInfectionSuspect = true;
    [SerializeField] private int highInfectionThreshold = 60;

    public List<SuspectRecord> GenerateLineupForDay(int currentDay)
    {
        List<SuspectRecord> lineup = new List<SuspectRecord>();
        List<SuspectRecord> candidates = suspectDatabase.GetAppearableRecords();

        if (candidates.Count == 0)
        {
            Debug.LogWarning("No suspects available to generate lineup.");
            return lineup;
        }

        List<SuspectRecord> pool = new List<SuspectRecord>(candidates);

        // Optional pacing rule: guarantee at least one more dangerous suspect.
        if (guaranteeAtLeastOneHighInfectionSuspect)
        {
            var highRiskPool = pool
                .Where(r => r.InfectionScore >= highInfectionThreshold)
                .ToList();

            if (highRiskPool.Count > 0)
            {
                var guaranteed = GetWeightedRandom(highRiskPool, currentDay);
                lineup.Add(guaranteed);
                pool.Remove(guaranteed);
            }
        }

        while (lineup.Count < suspectsPerDay && pool.Count > 0)
        {
            SuspectRecord chosen = GetWeightedRandom(pool, currentDay);

            if (chosen == null)
                break;

            lineup.Add(chosen);
            pool.Remove(chosen);
        }

        return lineup;
    }

    private SuspectRecord GetWeightedRandom(List<SuspectRecord> pool, int currentDay)
    {
        List<(SuspectRecord record, int weight)> weightedPool = new();

        foreach (var record in pool)
        {
            int weight = CalculateWeight(record, currentDay);

            if (weight > 0)
                weightedPool.Add((record, weight));
        }

        if (weightedPool.Count == 0)
            return null;

        int totalWeight = weightedPool.Sum(x => x.weight);
        int roll = Random.Range(0, totalWeight);

        int running = 0;
        foreach (var entry in weightedPool)
        {
            running += entry.weight;
            if (roll < running)
                return entry.record;
        }

        return weightedPool[weightedPool.Count - 1].record;
    }

    private int CalculateWeight(SuspectRecord record, int currentDay)
    {
        if (record == null || !record.CanAppear)
            return 0;

        int weight = baseWeight;

        // Unseen suspects help introduce variety.
        if (!record.HasBeenSeen)
            weight += unseenBonus;
        else
            weight += returningBonus;

        // The longer it's been since they appeared, the more likely they can return.
        int daysSinceSeen = currentDay - record.LastDaySeen;
        if (daysSinceSeen > 0)
            weight += daysSinceSeen * daysSinceSeenMultiplier;

        // If they were seen very recently, reduce chance.
        if (daysSinceSeen <= 1)
            weight -= recentlySeenPenalty;

        // Make worsening suspects more likely to reappear.
        if (record.InfectionScore >= highInfectionThreshold)
            weight += highInfectionBonus;

        if (record.InfectionScore >= 85)
            weight += criticalInfectionBonus;

        return Mathf.Max(0, weight);
    }
}