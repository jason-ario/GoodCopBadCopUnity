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

    [Header("Anomaly Distribution")]
    [Tooltip("Probability (0–1) that this suspect spawns with no anomalies.")]
    [SerializeField] [Range(0f, 1f)] private float _cleanChance = 0.2f;
    [Tooltip("Minimum number of anomalies when the suspect is not clean.")]
    [SerializeField] private int _minAnomalies = 1;
    [Tooltip("Maximum number of anomalies (inclusive) when the suspect is not clean.")]
    [SerializeField] private int _maxAnomalies = 2;

    private Anomaly[] _allPossibleAnomalies;
    public List<Anomaly> activeAnomalies = new List<Anomaly>();

    /// <summary>
    /// Stores the deterministic active indices chosen on the server for each
    /// RandomTentacleAnomaly, keyed by the anomaly's sibling index in the hierarchy.
    /// Used by SuspectCharacter to relay the selection to clients via ClientRpc.
    /// </summary>
    public Dictionary<int, int[]> TentacleAnomalyIndices { get; } = new Dictionary<int, int[]>();

    /// <summary>
    /// Stores the deterministic active indices chosen on the server for each
    /// RandomTumorAnomaly, keyed by the anomaly's sibling index in the hierarchy.
    /// Used by SuspectCharacter to relay the selection to clients via ClientRpc.
    /// </summary>
    public Dictionary<int, int[]> TumorAnomalyIndices { get; } = new Dictionary<int, int[]>();

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
        // Chance (0–1) that this suspect spawns with no anomalies at all.
        if (Random.value < _cleanChance)
        {
            Debug.Log("Suspect is clean — no anomalies activated.");
            return;
        }

        int anomalyCount = Random.Range(_minAnomalies, _maxAnomalies + 1);

        for (int i = 0; i < anomalyCount; i++)
        {
            if (_allPossibleAnomalies.Length == 0) break;

            Anomaly anomaly = _allPossibleAnomalies[Random.Range(0, _allPossibleAnomalies.Length)];
            
            // Skip if this anomaly is already active
            if (activeAnomalies.Contains(anomaly))
            {
                i--;
                continue;
            }
            
            Debug.Log("Activated " + anomaly.name);
            activeAnomalies.Add(anomaly);

            // For tentacle anomalies, pick indices on the server side and store them
            // so SuspectCharacter can relay the exact selection to clients.
            if (anomaly is RandomTentacleAnomaly tentacleAnomaly)
            {
                int[] indices = tentacleAnomaly.PickActiveIndices();
                TentacleAnomalyIndices[anomaly.transform.GetSiblingIndex()] = indices;
                tentacleAnomaly.ActivateWithIndices(indices);
            }
            else if (anomaly is RandomTumorAnomaly tumorAnomaly)
            {
                int[] indices = tumorAnomaly.PickActiveIndices();
                TumorAnomalyIndices[anomaly.transform.GetSiblingIndex()] = indices;
                tumorAnomaly.ActivateWithIndices(indices);
            }
            else
            {
                anomaly.ActivateAnomaly();
            }
        }
    }
    
    /// <summary>
    /// Applies tentacle indices that were chosen on the server. Called on clients
    /// after receiving the synced index data from SuspectCharacter.
    /// </summary>
    public void ApplyTentacleIndicesOnClient(int siblingIndex, int[] indices)
    {
        RandomTentacleAnomaly tentacleAnomaly = GetComponentsInChildren<RandomTentacleAnomaly>(true)
            .FirstOrDefault(t => t.transform.GetSiblingIndex() == siblingIndex);

        if (tentacleAnomaly != null)
            tentacleAnomaly.ActivateWithIndices(indices);
        else
            Debug.LogWarning($"[AnomalyController] No RandomTentacleAnomaly found at sibling index {siblingIndex}.");
    }

    /// <summary>
    /// Applies tumor indices that were chosen on the server. Called on clients
    /// after receiving the synced index data from SuspectCharacter.
    /// </summary>
    public void ApplyTumorIndicesOnClient(int siblingIndex, int[] indices)
    {
        RandomTumorAnomaly tumorAnomaly = GetComponentsInChildren<RandomTumorAnomaly>(true)
            .FirstOrDefault(t => t.transform.GetSiblingIndex() == siblingIndex);

        if (tumorAnomaly != null)
            tumorAnomaly.ActivateWithIndices(indices);
        else
            Debug.LogWarning($"[AnomalyController] No RandomTumorAnomaly found at sibling index {siblingIndex}.");
    }

    public bool HasAnomaly(Anomaly anomaly) => activeAnomalies.Contains(anomaly);
}