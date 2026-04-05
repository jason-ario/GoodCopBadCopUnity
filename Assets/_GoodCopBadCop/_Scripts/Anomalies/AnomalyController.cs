using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnomalyController : MonoBehaviour
{
    [SerializeField] private List<MutationAnomaly> _mutationAnomalies;
    [SerializeField] private List<BehaviorAnomaly> _behaviorAnomalies;
    [SerializeField] private List<BiologicalAnomaly> _biologicalAnomalies;
    [SerializeField] private List<DocumentationAnomaly> _documentationAnomalies;
    [SerializeField] private List<RealityDistortionAnomaly> _realityDistortionAnomalies;
    [SerializeField] private List<EnvironmentalAnomaly> _environmentalAnomalies;

    private Anomaly[] _anomalies;
    private List<Anomaly> _activeAnomalies = new List<Anomaly>();

    private void Awake()
    {
        _anomalies = _mutationAnomalies.Cast<Anomaly>()
            .Concat(_behaviorAnomalies)
            .Concat(_biologicalAnomalies)
            .Concat(_documentationAnomalies)
            .Concat(_realityDistortionAnomalies)
            .Concat(_environmentalAnomalies)
            .ToArray();
    }

    public void GenerateAndApplyAnomalies(int targetScore, int tolerance = 5, int maxPicks = 10)
    {
        //ClearAnomalies();

        List<Anomaly> chosen = GenerateAnomaliesForScore(targetScore, tolerance, maxPicks);

        foreach (Anomaly anomaly in chosen)
        {
            anomaly.ActivateAnomaly();
            _activeAnomalies.Add(anomaly);
        }
    }

    public List<Anomaly> GenerateAnomaliesForScore(int targetScore, int tolerance = 5, int maxPicks = 10)
    {
        List<Anomaly> chosen = new List<Anomaly>();

        List<Anomaly> candidates = _anomalies
            .Where(a => a.CanAppearForScore(targetScore))
            .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"No anomaly candidates found for infection score {targetScore} on {gameObject.name}");
            return chosen;
        }

        int currentTotal = 0;
        int safety = 0;

        while (currentTotal < targetScore - tolerance && chosen.Count < maxPicks && safety < 100)
        {
            safety++;

            List<Anomaly> validChoices = candidates
                .Where(a => !chosen.Contains(a) && currentTotal + a.ScoreValue <= targetScore + tolerance)
                .ToList();

            if (validChoices.Count == 0)
                break;

            Anomaly picked = GetWeightedRandom(validChoices);
            chosen.Add(picked);
            currentTotal += picked.ScoreValue;
        }

        return chosen;
    }

    private Anomaly GetWeightedRandom(List<Anomaly> anomalies)
    {
        int totalWeight = anomalies.Sum(a => Mathf.Max(1, a.SelectionWeight));
        int roll = Random.Range(0, totalWeight);

        int running = 0;
        foreach (Anomaly anomaly in anomalies)
        {
            running += Mathf.Max(1, anomaly.SelectionWeight);
            if (roll < running)
                return anomaly;
        }

        return anomalies[anomalies.Count - 1];
    }

    public void ClearAnomalies()
    {
        foreach (Anomaly anomaly in _anomalies)
        {
            anomaly.DeactivateAnomaly();
        }
    }
    
    public bool HasAnomaly(Anomaly anomaly) => _activeAnomalies.Contains(anomaly);
}