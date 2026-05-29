using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public class AnomalyController : MonoBehaviour
{
    [SerializeField] private List<MutationAnomaly> _mutationAnomalies;
    [SerializeField] private List<BiologicalAnomaly> _biologicalAnomalies;
    [SerializeField] private List<DocumentationAnomaly> _documentationAnomalies;

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

    /// <summary>
    /// Sibling indices of every anomaly on which InitializeDisabled was called during the
    /// most recent Initialize* pass. Used by SuspectCharacter to relay the call to clients.
    /// </summary>
    public List<int> DisabledAnomalySiblingIndices { get; } = new List<int>();

    public void Initialize()
    {
        DisabledAnomalySiblingIndices.Clear();
        var anomalies = new List<Anomaly>();

        if (!AnomalyManager.Instance.mutationAnomaliesLocked)
        {
            Debug.Log("Mutations are enabled");
            anomalies.AddRange(_mutationAnomalies.Cast<Anomaly>());
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

        _allPossibleAnomalies = anomalies.ToArray();
        Debug.Log("Activated anomalies");
        ActivateAnomalies();
    }

    /// <summary>
    /// Bypasses the clean-chance roll and forces exactly <paramref name="count"/> anomalies
    /// to be chosen from the currently unlocked pool. Use for tutorial suspects that must
    /// always have a specific number of anomalies.
    /// </summary>
    /// <param name="count">Exact number of anomalies to activate.</param>
    public void InitializeWithExactAnomalyCount(int count)
    {
        DisabledAnomalySiblingIndices.Clear();
        var anomalies = new List<Anomaly>();

        if (!AnomalyManager.Instance.mutationAnomaliesLocked)
            anomalies.AddRange(_mutationAnomalies.Cast<Anomaly>());
        if (!AnomalyManager.Instance.biologicalAnomaliesLocked)
            anomalies.AddRange(_biologicalAnomalies.Cast<Anomaly>());
        if (!AnomalyManager.Instance.documentationAnomaliesLocked)
            anomalies.AddRange(_documentationAnomalies.Cast<Anomaly>());

        _allPossibleAnomalies = anomalies.ToArray();

        int clamped = Mathf.Min(count, _allPossibleAnomalies.Length);
        for (int i = 0; i < clamped; i++)
        {
            if (_allPossibleAnomalies.Length == 0) break;

            Anomaly anomaly = _allPossibleAnomalies[Random.Range(0, _allPossibleAnomalies.Length)];
            if (activeAnomalies.Contains(anomaly)) { i--; continue; }

            activeAnomalies.Add(anomaly);
            Debug.Log($"[AnomalyController] Forced anomaly: {anomaly.name}");

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

        foreach (Anomaly anomaly in _allPossibleAnomalies)
        {
            if (!activeAnomalies.Contains(anomaly))
                InitializeDisabled(anomaly);
        }
    }

    /// <summary>
    /// Skips all anomaly assignment and ensures every anomaly visual is disabled.
    /// Use this to guarantee a suspect spawns completely clean — no random roll.
    /// </summary>
    public void InitializeClean()
    {
        DisabledAnomalySiblingIndices.Clear();
        _allPossibleAnomalies = CollectAllAnomalies();

        foreach (Anomaly anomaly in _allPossibleAnomalies)
            InitializeDisabled(anomaly);

        Debug.Log("[AnomalyController] Suspect forced clean — all anomalies disabled.");
    }

    private Anomaly[] CollectAllAnomalies()
    {
        var all = new List<Anomaly>();
        all.AddRange(_mutationAnomalies.Cast<Anomaly>());
        all.AddRange(_biologicalAnomalies.Cast<Anomaly>());
        all.AddRange(_documentationAnomalies.Cast<Anomaly>());
        return all.ToArray();
    }

    public void ActivateAnomalies()
    {
        // Chance (0–1) that this suspect spawns with no anomalies at all.
        if (Random.value < _cleanChance)
        {
            Debug.Log("Suspect is clean — no anomalies activated.");

            foreach (Anomaly anomaly in _allPossibleAnomalies)
                InitializeDisabled(anomaly);

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

        // Ensure every anomaly that was not selected has its shader state explicitly cleared.
        foreach (Anomaly anomaly in _allPossibleAnomalies)
        {
            if (!activeAnomalies.Contains(anomaly))
                InitializeDisabled(anomaly);
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

    /// <summary>
    /// Calls InitializeDisabled on the anomaly identified by <paramref name="siblingIndex"/>.
    /// Invoked on clients by SuspectCharacter after receiving SyncInitializeDisabledClientRpc.
    /// </summary>
    public void ApplyInitializeDisabledOnClient(int siblingIndex)
    {
        Anomaly anomaly = GetComponentsInChildren<Anomaly>(true)
            .FirstOrDefault(a => a.transform.GetSiblingIndex() == siblingIndex);

        if (anomaly != null)
            anomaly.InitializeDisabled();
        else
            Debug.LogWarning($"[AnomalyController] No Anomaly found at sibling index {siblingIndex} for InitializeDisabled.");
    }

    /// <summary>
    /// Re-applies InitializeDisabled on every non-active anomaly across all categories,
    /// including those from locked categories that were excluded during the initial activation
    /// pass. Call this when the suspect arrives at the booth to guarantee all shader states
    /// are clean regardless of which anomaly categories were locked at spawn time.
    /// </summary>
    public void InitializeDisabledOnArrival()
    {
        foreach (Anomaly anomaly in CollectAllAnomalies())
        {
            if (!activeAnomalies.Contains(anomaly))
                anomaly.InitializeDisabled();
        }
    }

    public bool HasAnomaly(Anomaly anomaly) => activeAnomalies.Contains(anomaly);

    /// <summary>
    /// Returns the number of currently active anomalies of the given category type.
    /// </summary>
    public int ActiveCountOfType<T>() where T : Anomaly
        => activeAnomalies.OfType<T>().Count();

    /// <summary>
    /// Calls InitializeDisabled on an anomaly and records its sibling index so
    /// SuspectCharacter can relay the call to clients via ClientRpc.
    /// </summary>
    private void InitializeDisabled(Anomaly anomaly)
    {
        anomaly.InitializeDisabled();
        DisabledAnomalySiblingIndices.Add(anomaly.transform.GetSiblingIndex());
    }
}
