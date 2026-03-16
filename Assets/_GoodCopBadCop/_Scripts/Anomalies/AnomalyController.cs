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
    private readonly List<Anomaly> _availableAnomalies = new List<Anomaly>();
    public int AvailableAnomalyCount => _availableAnomalies.Count;

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

    public void ResetAvailableAnomalies()
    {
        _availableAnomalies.Clear();
        _availableAnomalies.AddRange(_anomalies);
    }

    public void TriggerAnomaly()
    {
        if (_availableAnomalies.Count == 0)
        {
            Debug.LogWarning("No available anomalies left to trigger this round.");
            return;
        }

        int index = Random.Range(0, _availableAnomalies.Count);
        Anomaly chosen = _availableAnomalies[index];
        _availableAnomalies.RemoveAt(index);
        chosen.ActivateAnomaly();
    }
}