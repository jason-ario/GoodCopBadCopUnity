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
    public void TriggerAnomaly()
    {
        var randomAnomaly = _anomalies[Random.Range(0, _anomalies.Length)];
        randomAnomaly.ActivateAnomaly();
    }
}
