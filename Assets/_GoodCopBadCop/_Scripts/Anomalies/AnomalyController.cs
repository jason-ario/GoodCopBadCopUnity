using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public class AnomalyController : MonoBehaviour
{
    [SerializeField] private List<MutationAnomaly> _mutationAnomalies;
    [SerializeField] private List<BehaviorAnomaly> _behaviorAnomalies;
    [SerializeField] private List<BiologicalAnomaly> _biologicalAnomalies;
    [SerializeField] private List<DocumentationAnomaly> _documentationAnomalies;
    [SerializeField] private List<EnvironmentalAnomaly> _environmentalAnomalies;

    private Anomaly[] _allPossibleAnomalies;
    public List<Anomaly> activeAnomalies = new List<Anomaly>();

    private int infectionScore = 10; // make this increase over time or go down if they're quarantined

    public void Initialize()
    {
        var anomalies = new List<Anomaly>();

        if (!AnomalyManager.Instance.mutationAnomaliesLocked)
        {
            Debug.Log("Mutations are enabled");
            anomalies.AddRange(_mutationAnomalies.Cast<Anomaly>());
        }

        if (!AnomalyManager.Instance.behaviorAnomaliesLocked)
        {
            Debug.Log("Behavior is enabled");
            anomalies.AddRange(_behaviorAnomalies.Cast<Anomaly>());
        }

        if (!AnomalyManager.Instance.biologicalAnomaliesLocked)
        {
            Debug.Log("Biological is enabled");
            anomalies.AddRange(_biologicalAnomalies.Cast<Anomaly>());
        }

        if (!AnomalyManager.Instance.documentationAnomaliesLocked)
        {
            Debug.Log("Documentation is enabled");
            anomalies.AddRange(_documentationAnomalies.Cast<Anomaly>());
        }

        if (!AnomalyManager.Instance.environmentAnomaliesLocked)
        {
            Debug.Log("Environment is enabled");
            anomalies.AddRange(_environmentalAnomalies.Cast<Anomaly>());
        }

        _allPossibleAnomalies = anomalies.ToArray(); 
        Debug.Log("Activated anomalies");
        ActivateAnomalies();
    }

    public void ActivateAnomalies()
    {
        int anomalyCount = Random.Range(2, 3);

        for (int i = 0; i < anomalyCount; i++)
        {
            Anomaly anomaly = _allPossibleAnomalies[Random.Range(0, _allPossibleAnomalies.Length)];
            Debug.Log("Activated" + anomaly.name);
            activeAnomalies.Add(anomaly);
            anomaly.ActivateAnomaly();
        }
    }
    
    public bool HasAnomaly(Anomaly anomaly) => activeAnomalies.Contains(anomaly);
}