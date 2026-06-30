using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AnomalyController : MonoBehaviour
{
    // ── Category Pools ────────────────────────────────────────────────────────
    // Each list holds all authored anomaly components for one category on this prefab.
    // Order within a list determines presentation priority: index 0 activates first
    // as the infection score rises. Arrange subtler anomalies first, overt ones last.

    [Header("Anomaly Pools — 5 Categories")]
    [SerializeField] private List<DocumentationAnomaly> _documentationAnomalies = new List<DocumentationAnomaly>();
    [SerializeField] private List<VitalsAnomaly> _vitalsAnomalies = new List<VitalsAnomaly>();
    [SerializeField] private List<BehaviorAnomaly> _behaviorAnomalies = new List<BehaviorAnomaly>();
    [SerializeField] private List<PhysicalAnomaly> _mutationAnomalies = new List<PhysicalAnomaly>();
    [SerializeField] private List<SupernaturalAnomaly> _supernaturalAnomalies = new List<SupernaturalAnomaly>();

    // ── Thresholds ────────────────────────────────────────────────────────────

    /// <summary>
    /// Infection score at or above which a suspect is considered too far gone:
    /// all anomalies activate and quarantine has no effect.
    /// </summary>
    public const int FULLY_MUTATED_THRESHOLD = 80;

    // ── Runtime State ─────────────────────────────────────────────────────────

    /// <summary>All anomalies currently visible on this suspect.</summary>
    public List<Anomaly> activeAnomalies = new List<Anomaly>();

    /// <summary>
    /// Deterministic active indices per RandomTentacleAnomaly, keyed by sibling index.
    /// Used by SuspectCharacter to relay server selections to clients.
    /// </summary>
    public Dictionary<int, int[]> TentacleAnomalyIndices { get; } = new Dictionary<int, int[]>();

    /// <summary>
    /// Deterministic active indices per RandomTumorAnomaly, keyed by sibling index.
    /// Used by SuspectCharacter to relay server selections to clients.
    /// </summary>
    public Dictionary<int, int[]> TumorAnomalyIndices { get; } = new Dictionary<int, int[]>();

    /// <summary>
    /// Sibling indices of every anomaly that had InitializeDisabled called on it.
    /// Used by SuspectCharacter to relay the call to clients.
    /// </summary>
    public List<int> DisabledAnomalySiblingIndices { get; } = new List<int>();

    // ── Primary Score-Based API ───────────────────────────────────────────────

    /// <summary>
    /// Activates anomalies using a two-dimensional random strategy:
    ///
    ///   Dimension 1 — which categories are active. Below <see cref="FULLY_MUTATED_THRESHOLD"/>
    ///   exactly <see cref="CATEGORY_CAP_BELOW_THRESHOLD"/> (4) populated categories are chosen
    ///   at random, leaving one dark. At or above the threshold every populated category
    ///   activates, which is the observable "too far gone" signal.
    ///
    ///   Dimension 2 — how many anomalies within each active category. Anomalies are shuffled
    ///   randomly within their pool and the count scales proportionally with the score, with
    ///   a minimum of 1 per active category.
    ///
    /// Score 0 delegates to <see cref="InitializeClean"/>.
    /// </summary>
    public void InitializeByInfectionScore(int infectionScore)
    {
        if (infectionScore <= 0)
        {
            InitializeClean();
            return;
        }

        DisabledAnomalySiblingIndices.Clear();
        activeAnomalies.Clear();

        // Build a list of non-empty category pools. Characters without anomalies in a
        // given category simply have an empty list; excluding them keeps the maths correct.
        var categoryPools = new List<List<Anomaly>>
        {
            _documentationAnomalies.Cast<Anomaly>().ToList(),
            _vitalsAnomalies.Cast<Anomaly>().ToList(),
            _behaviorAnomalies.Cast<Anomaly>().ToList(),
            _mutationAnomalies.Cast<Anomaly>().ToList(),
            _supernaturalAnomalies.Cast<Anomaly>().ToList(),
        };
        categoryPools.RemoveAll(p => p.Count == 0);

        if (categoryPools.Count == 0)
        {
            Debug.LogWarning("[AnomalyController] No anomalies configured — nothing to activate.");
            return;
        }

        bool fullyMutated = infectionScore >= FULLY_MUTATED_THRESHOLD;

        // ── Dimension 1: randomly choose which categories are active ──────────
        // Shuffle the pool list so the first N entries are the "active" ones.
        // Below the threshold exactly CATEGORY_CAP_BELOW_THRESHOLD categories are active;
        // at/above it every populated category is active.
        ShuffleList(categoryPools);
        int activeCategoryCount = fullyMutated
            ? categoryPools.Count
            : Mathf.Min(CATEGORY_CAP_BELOW_THRESHOLD, categoryPools.Count);

        // ── Dimension 2: randomly pick and activate anomalies within each category ──
        int totalActivated = 0;

        for (int c = 0; c < categoryPools.Count; c++)
        {
            List<Anomaly> pool = categoryPools[c];

            if (c >= activeCategoryCount)
            {
                // This category is inactive for this spawn — disable all its anomalies.
                foreach (Anomaly a in pool)
                    InitializeDisabled(a);
                continue;
            }

            // Shuffle anomalies within the category so the active subset is random,
            // not biased toward Inspector list order.
            ShuffleList(pool);

            int countInCategory = fullyMutated
                ? pool.Count
                : Mathf.Max(1, Mathf.FloorToInt((float)infectionScore / FULLY_MUTATED_THRESHOLD * pool.Count));

            for (int i = 0; i < pool.Count; i++)
            {
                if (i < countInCategory)
                {
                    ActivateAnomaly(pool[i]);
                    totalActivated++;
                }
                else
                {
                    InitializeDisabled(pool[i]);
                }
            }
        }

        Debug.Log($"[AnomalyController] Score {infectionScore} → " +
                  $"{activeCategoryCount}/{categoryPools.Count} categories, " +
                  $"{totalActivated} anomaly/ies active." +
                  (fullyMutated ? " (FULLY MUTATED)" : string.Empty));
    }

    /// <summary>
    /// Maximum categories that may be active while the suspect is below
    /// <see cref="FULLY_MUTATED_THRESHOLD"/>. One category always stays dark until
    /// the suspect is truly too far gone.
    /// </summary>
    private const int CATEGORY_CAP_BELOW_THRESHOLD = 4;

    /// <summary>
    /// True when every populated anomaly category has at least one active anomaly.
    /// This is the observable in-game signal that the suspect is past the point of no return —
    /// all five checklist categories will show a positive result simultaneously.
    /// </summary>
    public bool IsFullyMutated
    {
        get
        {
            if (activeAnomalies.Count == 0) return false;
            if (_documentationAnomalies.Count > 0 && !HasActiveAnomalyOfCategory("DocumentationAnomaly")) return false;
            if (_vitalsAnomalies.Count      > 0 && !HasActiveAnomalyOfCategory("VitalsAnomaly"))        return false;
            if (_behaviorAnomalies.Count    > 0 && !HasActiveAnomalyOfCategory("BehaviorAnomaly"))      return false;
            if (_mutationAnomalies.Count    > 0 && !HasActiveAnomalyOfCategory("MutationAnomaly"))      return false;
            if (_supernaturalAnomalies.Count > 0 && !HasActiveAnomalyOfCategory("SupernaturalAnomaly")) return false;
            return true;
        }
    }

    // ── Tutorial / Forced-State API ───────────────────────────────────────────

    /// <summary>
    /// Forces every anomaly on this suspect to activate regardless of infection score.
    /// Used for doppelgangers and any scenario requiring a full-anomaly loadout.
    /// </summary>
    public void Initialize()
    {
        InitializeByInfectionScore(100);
    }

    /// <summary>
    /// Forces exactly <paramref name="count"/> anomalies chosen at random from all pools.
    /// Bypasses score logic entirely. Use for tutorial suspects that must exhibit a specific count.
    /// </summary>
    public void InitializeWithExactAnomalyCount(int count)
    {
        DisabledAnomalySiblingIndices.Clear();
        activeAnomalies.Clear();

        Anomaly[] all = CollectAllAnomalies();
        int clamped = Mathf.Min(count, all.Length);

        // Fisher-Yates partial shuffle to pick `clamped` unique anomalies.
        List<Anomaly> pool = new List<Anomaly>(all);
        for (int i = 0; i < clamped; i++)
        {
            int j = Random.Range(i, pool.Count);
            (pool[i], pool[j]) = (pool[j], pool[i]);
            ActivateAnomaly(pool[i]);
        }

        for (int i = clamped; i < pool.Count; i++)
            InitializeDisabled(pool[i]);
    }

    /// <summary>
    /// Activates exactly <paramref name="count"/> anomalies from the documentation pool only.
    /// All other categories are fully disabled. Used for tutorial suspects (e.g. Ivan on Day 1)
    /// that must exhibit documentation discrepancies and nothing else.
    /// </summary>
    public void InitializeWithDocumentationAnomalies(int count)
    {
        DisabledAnomalySiblingIndices.Clear();
        activeAnomalies.Clear();

        // Disable every non-documentation anomaly first.
        var others = new System.Collections.Generic.List<Anomaly>();
        others.AddRange(_vitalsAnomalies.Cast<Anomaly>());
        others.AddRange(_behaviorAnomalies.Cast<Anomaly>());
        others.AddRange(_mutationAnomalies.Cast<Anomaly>());
        others.AddRange(_supernaturalAnomalies.Cast<Anomaly>());
        foreach (Anomaly a in others) InitializeDisabled(a);

        // Shuffle and activate the requested count from the documentation pool.
        var docPool = _documentationAnomalies.Cast<Anomaly>().ToList();
        ShuffleList(docPool);
        int toActivate = Mathf.Min(count, docPool.Count);
        for (int i = 0; i < toActivate; i++) ActivateAnomaly(docPool[i]);
        for (int i = toActivate; i < docPool.Count; i++) InitializeDisabled(docPool[i]);

        Debug.Log($"[AnomalyController] Documentation-only init: {toActivate}/{docPool.Count} anomaly/ies active.");
    }

    /// <summary>
    /// Disables every anomaly without any transition. Guarantees a clean suspect regardless
    /// of prior state.
    /// </summary>
    public void InitializeClean()
    {
        DisabledAnomalySiblingIndices.Clear();
        activeAnomalies.Clear();

        foreach (Anomaly anomaly in CollectAllAnomalies())
            InitializeDisabled(anomaly);

        Debug.Log("[AnomalyController] Suspect forced clean — all anomalies disabled.");
    }

    // ── Client Sync Helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Applies tentacle indices chosen on the server to the matching anomaly on this client.
    /// </summary>
    public void ApplyTentacleIndicesOnClient(int siblingIndex, int[] indices)
    {
        RandomTentacleAnomaly tentacleAnomaly = GetComponentsInChildren<RandomTentacleAnomaly>(true)
            .FirstOrDefault(t => t.transform.GetSiblingIndex() == siblingIndex);

        if (tentacleAnomaly != null)
            tentacleAnomaly.ActivateWithIndices(indices);
        else
            Debug.LogWarning($"[AnomalyController] No RandomTentacleAnomaly at sibling index {siblingIndex}.");
    }

    /// <summary>
    /// Applies tumor indices chosen on the server to the matching anomaly on this client.
    /// </summary>
    public void ApplyTumorIndicesOnClient(int siblingIndex, int[] indices)
    {
        RandomTumorAnomaly tumorAnomaly = GetComponentsInChildren<RandomTumorAnomaly>(true)
            .FirstOrDefault(t => t.transform.GetSiblingIndex() == siblingIndex);

        if (tumorAnomaly != null)
            tumorAnomaly.ActivateWithIndices(indices);
        else
            Debug.LogWarning($"[AnomalyController] No RandomTumorAnomaly at sibling index {siblingIndex}.");
    }

    /// <summary>
    /// Calls InitializeDisabled on the anomaly at <paramref name="siblingIndex"/>.
    /// Invoked on clients after receiving SyncInitializeDisabledClientRpc.
    /// </summary>
    public void ApplyInitializeDisabledOnClient(int siblingIndex)
    {
        Anomaly anomaly = GetComponentsInChildren<Anomaly>(true)
            .FirstOrDefault(a => a.transform.GetSiblingIndex() == siblingIndex);

        if (anomaly != null)
            anomaly.InitializeDisabled();
        else
            Debug.LogWarning($"[AnomalyController] No Anomaly at sibling index {siblingIndex} for InitializeDisabled.");
    }

    /// <summary>
    /// Re-applies InitializeDisabled on every non-active anomaly.
    /// Call on suspect arrival to ensure shader states are clean for locked-category anomalies.
    /// </summary>
    public void InitializeDisabledOnArrival()
    {
        foreach (Anomaly anomaly in CollectAllAnomalies())
        {
            if (!activeAnomalies.Contains(anomaly))
                anomaly.InitializeDisabled();
        }
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    /// <summary>Returns true if at least one active anomaly belongs to <paramref name="categoryTypeName"/>.</summary>
    public bool HasAnomaly(Anomaly anomaly) => activeAnomalies.Contains(anomaly);

    /// <summary>Returns the count of active anomalies belonging to category type <typeparamref name="T"/>.</summary>
    public int ActiveCountOfType<T>() where T : Anomaly
        => activeAnomalies.OfType<T>().Count();

    /// <summary>
    /// Returns true if any currently active anomaly is an instance of (or inherits from) the
    /// category class named <paramref name="categoryTypeName"/> (e.g. "MutationAnomaly").
    /// Walks the full type hierarchy so concrete subclasses are matched by their category base.
    /// </summary>
    public bool HasActiveAnomalyOfCategory(string categoryTypeName)
    {
        foreach (Anomaly anomaly in activeAnomalies)
        {
            System.Type t = anomaly.GetType();
            while (t != null && t != typeof(Anomaly))
            {
                if (t.Name == categoryTypeName) return true;
                t = t.BaseType;
            }
        }
        return false;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects all anomaly components from all five category lists.
    /// Used by InitializeClean, InitializeWithExactAnomalyCount, and InitializeDisabledOnArrival.
    /// </summary>
    private Anomaly[] CollectAllAnomalies()
    {
        var all = new List<Anomaly>();
        all.AddRange(_documentationAnomalies.Cast<Anomaly>());
        all.AddRange(_vitalsAnomalies.Cast<Anomaly>());
        all.AddRange(_behaviorAnomalies.Cast<Anomaly>());
        all.AddRange(_mutationAnomalies.Cast<Anomaly>());
        all.AddRange(_supernaturalAnomalies.Cast<Anomaly>());
        return all.ToArray();
    }

    /// <summary>Fisher-Yates in-place shuffle using Unity's Random.</summary>
    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Picks a random anomaly from the active list, deactivates it, and removes it.
    /// Returns the sibling index of the removed anomaly for client replication,
    /// or -1 if there are no active anomalies.
    /// </summary>
    public int RemoveRandomActiveAnomaly()
    {
        if (activeAnomalies.Count == 0) return -1;

        int index = Random.Range(0, activeAnomalies.Count);
        Anomaly anomaly = activeAnomalies[index];
        int siblingIndex = anomaly.transform.GetSiblingIndex();

        anomaly.DeactivateAnomaly();
        activeAnomalies.RemoveAt(index);

        Debug.Log($"[AnomalyController] Vaccine applied — deactivated '{anomaly.name}' (siblingIndex {siblingIndex}). " +
                  $"{activeAnomalies.Count} anomaly/ies remaining.");
        return siblingIndex;
    }

    /// <summary>
    /// Deactivates and removes the anomaly that has the given sibling index in the hierarchy.
    /// Used on non-server clients to replicate a server-chosen anomaly removal.
    /// </summary>
    public void RemoveAnomalyBySiblingIndex(int siblingIndex)
    {
        Anomaly target = null;
        foreach (Anomaly a in CollectAllAnomalies())
        {
            if (a.transform.GetSiblingIndex() == siblingIndex)
            {
                target = a;
                break;
            }
        }

        if (target == null)
        {
            Debug.LogWarning($"[AnomalyController] RemoveAnomalyBySiblingIndex: no anomaly at sibling index {siblingIndex}.");
            return;
        }

        target.DeactivateAnomaly();
        activeAnomalies.Remove(target);
    }

    /// <summary>
    /// Activates a single anomaly, handling RandomTentacleAnomaly and RandomTumorAnomaly
    /// special cases by picking and storing indices for client replication.
    /// </summary>
    private void ActivateAnomaly(Anomaly anomaly)
    {
        if (anomaly == null || activeAnomalies.Contains(anomaly)) return;

        activeAnomalies.Add(anomaly);

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

    /// <summary>
    /// Calls InitializeDisabled on an anomaly and records its sibling index for client replication.
    /// </summary>
    private void InitializeDisabled(Anomaly anomaly)
    {
        anomaly.InitializeDisabled();
        DisabledAnomalySiblingIndices.Add(anomaly.transform.GetSiblingIndex());
    }
}
