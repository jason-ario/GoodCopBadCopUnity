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
    /// Infection score (0–100 legacy scale) at or above which a suspect is considered fully
    /// mutated. At this threshold anomalies are suppressed entirely — the character's visual
    /// transformation is the only observable signal. Below the threshold, anomalies are chosen
    /// via the mutation-score points budget in <see cref="InitializeByMutationScore"/>.
    /// </summary>
    public const int FULLY_MUTATED_THRESHOLD = 80;

    // ── Runtime State ─────────────────────────────────────────────────────────

    /// <summary>All anomalies currently visible on this suspect.</summary>
    public List<Anomaly> activeAnomalies = new List<Anomaly>();

    /// <summary>
    /// Deterministic active indices per RandomTentacleAnomaly, keyed by anomaly id.
    /// Used by SuspectCharacter to relay server selections to clients.
    /// </summary>
    public Dictionary<int, int[]> TentacleAnomalyIndices { get; } = new Dictionary<int, int[]>();

    /// <summary>
    /// Deterministic active indices per RandomTumorAnomaly, keyed by anomaly id.
    /// Used by SuspectCharacter to relay server selections to clients.
    /// </summary>
    public Dictionary<int, int[]> TumorAnomalyIndices { get; } = new Dictionary<int, int[]>();

    /// <summary>
    /// Stable anomaly ids of every anomaly that had InitializeDisabled called on it.
    /// Used by SuspectCharacter to relay the call to clients.
    /// </summary>
    public List<int> DisabledAnomalyIds { get; } = new List<int>();

    // ── Primary Score-Based API ───────────────────────────────────────────────

    /// <summary>
    /// Activates anomalies for a suspect using a mutation-score point budget.
    ///
    /// <paramref name="infectionScore"/> is on the legacy persistent 0–100 scale; it is first
    /// converted to a 0–10 "mutation score" (10 = full mutant, 8 = will mutate that night,
    /// 5 = a fair quarantine amount) via <c>Mathf.RoundToInt(infectionScore / 10f)</c>.
    ///
    /// The mutation score is spent as a points budget across every unlocked anomaly, regardless
    /// of category. Each anomaly category has its own point cost — configured on
    /// <see cref="AnomalyManager"/> in the inspector (e.g. Physical/Supernatural cost 2, others
    /// cost 1 by default). Unlocked anomalies from every category are shuffled together into one
    /// pool and activated one at a time while affordable, so a score of 5 might buy 2 Physical
    /// anomalies + 1 Documentation anomaly, or 5 Documentation anomalies, depending on the shuffle
    /// and what's unlocked — never more than the budget allows.
    ///
    /// Only anomalies whose type name is unlocked in <see cref="AnomalyUnlockManager"/> are
    /// eligible for selection; locked anomalies are silently disabled and excluded from the pool.
    ///
    /// At or above <see cref="FULLY_MUTATED_THRESHOLD"/> the suspect is considered fully mutated
    /// and <see cref="InitializeClean"/> is called instead — no anomalies activate because the
    /// character's visual transformation is the only signal needed. Score 0 also delegates to
    /// <see cref="InitializeClean"/>.
    /// </summary>
    public void InitializeByInfectionScore(int infectionScore)
    {
        if (infectionScore <= 0)
        {
            InitializeClean();
            return;
        }

        if (infectionScore >= FULLY_MUTATED_THRESHOLD)
        {
            InitializeClean();
            Debug.Log($"[AnomalyController] Score {infectionScore} → fully mutated — anomalies suppressed.");
            return;
        }

        int mutationScore = Mathf.Clamp(Mathf.RoundToInt(infectionScore / 10f), 0, 10);
        InitializeByMutationScore(mutationScore);
    }

    /// <summary>
    /// Activates anomalies for a suspect using a 0–10 mutation-score points budget directly
    /// (10 = full mutant, 8 = will mutate that night, 5 = a fair quarantine amount).
    ///
    /// Every unlocked anomaly across all five categories is pooled together and shuffled; the
    /// budget is then spent by activating anomalies one at a time for as long as their category's
    /// point cost (see <see cref="AnomalyManager.GetPointCost"/>) still fits in the remaining
    /// budget. This naturally distributes points across categories at random rather than forcing
    /// an even split — a suspect can end up with anomalies from every category, or all its points
    /// spent on a single cheap category.
    ///
    /// Score 0 delegates to <see cref="InitializeClean"/>.
    /// </summary>
    public void InitializeByMutationScore(int mutationScore)
    {
        if (mutationScore <= 0)
        {
            InitializeClean();
            return;
        }

        ClearInitializationState(deactivateActive: true);

        // Build the combined, unlocked anomaly pool tagged with its category so we can look up
        // each anomaly's point cost. FilterToUnlocked calls InitializeDisabled on each locked
        // entry so they are both visually reset and recorded for client replication.
        var pool = new List<(Anomaly anomaly, AnomalyCategory category)>();
        AddCategoryToPool(FilterToUnlocked(_documentationAnomalies.Cast<Anomaly>().ToList()), AnomalyCategory.Documentation, pool);
        AddCategoryToPool(FilterToUnlocked(_vitalsAnomalies.Cast<Anomaly>().ToList()), AnomalyCategory.Vitals, pool);
        AddCategoryToPool(FilterToUnlocked(_behaviorAnomalies.Cast<Anomaly>().ToList()), AnomalyCategory.Behavior, pool);
        AddCategoryToPool(FilterToUnlocked(_mutationAnomalies.Cast<Anomaly>().ToList()), AnomalyCategory.Mutations, pool);
        AddCategoryToPool(FilterToUnlocked(_supernaturalAnomalies.Cast<Anomaly>().ToList()), AnomalyCategory.Supernatural, pool);

        if (pool.Count == 0)
        {
            Debug.LogWarning("[AnomalyController] No unlocked anomalies available — suspect spawns clean.");
            return;
        }

        ShuffleList(pool);

        // ── Spend the mutation-score points budget across the shuffled combined pool ──────
        int remainingBudget = mutationScore;
        int totalActivated = 0;
        var perCategoryCounts = new Dictionary<AnomalyCategory, int>();

        foreach ((Anomaly anomaly, AnomalyCategory category) entry in pool)
        {
            int cost = GetAnomalyPointCost(entry.category);

            if (cost <= remainingBudget)
            {
                ActivateAnomaly(entry.anomaly);
                remainingBudget -= cost;
                totalActivated++;
                perCategoryCounts.TryGetValue(entry.category, out int existing);
                perCategoryCounts[entry.category] = existing + 1;
            }
            else
            {
                InitializeDisabled(entry.anomaly);
            }
        }

        string breakdown = string.Join(", ", perCategoryCounts.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        Debug.Log($"[AnomalyController] Mutation score {mutationScore} → {totalActivated} anomaly/ies active " +
                  $"({breakdown}), {remainingBudget} point(s) unspent.");
    }

    /// <summary>
    /// Returns the point cost of a single anomaly in <paramref name="category"/>, sourced from
    /// <see cref="AnomalyManager.GetPointCost"/> when available. Falls back to the documented
    /// default costs (Physical/Supernatural = 2, Documentation/Behavior/Vitals = 1) when no
    /// <see cref="AnomalyManager"/> instance exists (e.g. in tests). Also used by
    /// <see cref="FolderScoreTab"/> to total checked checklist items with the same point values.
    /// </summary>
    public static int GetAnomalyPointCost(AnomalyCategory category)
    {
        if (AnomalyManager.Instance != null)
            return AnomalyManager.Instance.GetPointCost(category);

        return category switch
        {
            AnomalyCategory.Mutations    => 2,
            AnomalyCategory.Supernatural => 2,
            _                            => 1,
        };
    }

    private static void AddCategoryToPool(
        List<Anomaly> categoryAnomalies,
        AnomalyCategory category,
        List<(Anomaly anomaly, AnomalyCategory category)> pool)
    {
        foreach (Anomaly anomaly in categoryAnomalies)
            pool.Add((anomaly, category));
    }

    /// <summary>
    /// True when every populated anomaly category has at least one active anomaly.
    /// Score-based initialization never reaches this state for fully-mutated suspects
    /// (their anomalies are suppressed). This property is only true after a forced
    /// full-activation via <see cref="Initialize"/> (doppelgangers, debug tools).
    /// </summary>
    public bool IsFullyMutated
    {
        get
        {
            if (activeAnomalies.Count == 0) return false;
            if (_documentationAnomalies.Count > 0 && !HasActiveAnomalyOfCategory("DocumentationAnomaly")) return false;
            if (_vitalsAnomalies.Count      > 0 && !HasActiveAnomalyOfCategory("VitalsAnomaly"))        return false;
            if (_behaviorAnomalies.Count    > 0 && !HasActiveAnomalyOfCategory("BehaviorAnomaly"))      return false;
            if (_mutationAnomalies.Count    > 0 && !HasActiveAnomalyOfCategory("PhysicalAnomaly"))      return false;
            if (_supernaturalAnomalies.Count > 0 && !HasActiveAnomalyOfCategory("SupernaturalAnomaly")) return false;
            return true;
        }
    }

    // ── Tutorial / Forced-State API ───────────────────────────────────────────

    /// <summary>
    /// Forces every unlocked anomaly on this suspect to activate regardless of infection score.
    /// Bypasses the fully-mutated suppression guard in <see cref="InitializeByInfectionScore"/>.
    /// Legacy entry point — prefer <see cref="InitializeByInfectionScore"/> for all new call sites.
    /// </summary>
    public void Initialize()
    {
        ClearInitializationState(deactivateActive: true);

        var categoryPools = new List<List<Anomaly>>
        {
            FilterToUnlocked(_documentationAnomalies.Cast<Anomaly>().ToList()),
            FilterToUnlocked(_vitalsAnomalies.Cast<Anomaly>().ToList()),
            FilterToUnlocked(_behaviorAnomalies.Cast<Anomaly>().ToList()),
            FilterToUnlocked(_mutationAnomalies.Cast<Anomaly>().ToList()),
            FilterToUnlocked(_supernaturalAnomalies.Cast<Anomaly>().ToList()),
        };
        categoryPools.RemoveAll(p => p.Count == 0);

        int totalActivated = 0;
        foreach (List<Anomaly> pool in categoryPools)
        {
            foreach (Anomaly a in pool)
            {
                ActivateAnomaly(a);
                totalActivated++;
            }
        }

        Debug.Log($"[AnomalyController] Initialize() → {totalActivated} unlocked anomaly/ies active (all categories forced).");
    }

    /// <summary>
    /// Forces exactly <paramref name="count"/> anomalies chosen at random from all pools.
    /// Bypasses score logic entirely. Use for tutorial suspects that must exhibit a specific count.
    /// Only anomalies unlocked in <see cref="AnomalyUnlockManager"/> are eligible.
    /// </summary>
    public void InitializeWithExactAnomalyCount(int count)
    {
        ClearInitializationState(deactivateActive: true);

        Anomaly[] all = CollectAllAnomalies();
        List<Anomaly> pool = FilterToUnlocked(new List<Anomaly>(all));
        int clamped = Mathf.Min(count, pool.Count);

        // Fisher-Yates partial shuffle to pick `clamped` unique anomalies.
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
    /// Used for tutorial suspects (e.g. the Day 1 quarantine tutorial suspect) that must exhibit paperwork
    /// discrepancies. Other categories stay unavailable through the normal unlock rules.
    /// Only unlocked documentation anomalies are eligible for selection.
    /// <para>
    /// <see cref="MissingDocumentAnomaly"/> is always excluded from selection here: when active,
    /// <see cref="SuspectPaperworkService.BuildForSuspect"/> reports <c>DocumentsVisible = false</c>,
    /// which makes <see cref="SuspectController.SpawnPaperwork"/> abort entirely and spawn no
    /// documents at all — self-defeating for a tutorial suspect who must have physical documents
    /// on the desk for the player to inspect.
    /// </para>
    /// </summary>
    public void InitializeWithDocumentationAnomalies(int count)
    {
        ClearInitializationState(deactivateActive: true);

        // Filter to unlocked documentation anomalies, excluding MissingDocumentAnomaly (see summary),
        // then shuffle and activate.
        var docPool = FilterToUnlocked(_documentationAnomalies.Cast<Anomaly>().ToList())
            .Where(a => a is not MissingDocumentAnomaly)
            .ToList();
        ShuffleList(docPool);
        int toActivate = Mathf.Min(count, docPool.Count);
        for (int i = 0; i < toActivate; i++) ActivateAnomaly(docPool[i]);
        for (int i = toActivate; i < docPool.Count; i++) InitializeDisabled(docPool[i]);

        Debug.Log($"[AnomalyController] Documentation-only init: {toActivate}/{docPool.Count} unlocked anomaly/ies active (MissingDocumentAnomaly excluded).");
    }

    /// <summary>
    /// Activates exactly <paramref name="count"/> anomalies chosen at random from the combined
    /// documentation and mutation (physical) pools only. Every other category stays unavailable,
    /// exactly like <see cref="InitializeWithDocumentationAnomalies"/>. Used for scripted suspects
    /// that must exhibit both a paperwork discrepancy and a visible physical mutation.
    /// <para>
    /// <see cref="MissingDocumentAnomaly"/> is always excluded from selection here for the same
    /// reason as <see cref="InitializeWithDocumentationAnomalies"/>: it would make
    /// <see cref="SuspectController.SpawnPaperwork"/> abort and spawn no documents at all.
    /// </para>
    /// </summary>
    public void InitializeWithDocumentationAndPhysicalAnomalies(int count)
    {
        ClearInitializationState(deactivateActive: true);

        var pool = FilterToUnlocked(
                _documentationAnomalies.Cast<Anomaly>()
                    .Concat(_mutationAnomalies.Cast<Anomaly>())
                    .ToList())
            .Where(a => a is not MissingDocumentAnomaly)
            .ToList();

        ShuffleList(pool);
        int toActivate = Mathf.Min(count, pool.Count);
        for (int i = 0; i < toActivate; i++) ActivateAnomaly(pool[i]);
        for (int i = toActivate; i < pool.Count; i++) InitializeDisabled(pool[i]);

        Debug.Log($"[AnomalyController] Documentation+Physical init: {toActivate}/{pool.Count} unlocked anomaly/ies active (MissingDocumentAnomaly excluded).");
    }

    /// <summary>
    /// Forces on exactly the anomaly types named in <paramref name="typeNames"/> (matched against
    /// each anomaly component's C# type name, e.g. "RandomTumorAnomaly"), bypassing
    /// <see cref="AnomalyUnlockManager"/> entirely. Every other anomaly on the prefab is disabled.
    /// Used for scripted "too far gone" tutorial suspects that must visibly exhibit anomalies
    /// that are not yet unlocked for normal gameplay.
    /// </summary>
    public void InitializeWithForcedAnomalyTypes(IEnumerable<string> typeNames)
    {
        ClearInitializationState(deactivateActive: true);

        var wanted = new HashSet<string>(typeNames ?? System.Array.Empty<string>(), System.StringComparer.Ordinal);
        int totalActivated = 0;

        foreach (Anomaly anomaly in CollectAllAnomalies())
        {
            if (anomaly == null) continue;

            if (wanted.Contains(anomaly.GetType().Name))
            {
                ActivateAnomaly(anomaly);
                totalActivated++;
            }
            else
            {
                InitializeDisabled(anomaly);
            }
        }

        Debug.Log($"[AnomalyController] Forced anomaly types init: {totalActivated} anomaly/ies active " +
                  $"(bypassing unlock gate) — requested types: {string.Join(", ", wanted)}.");
    }

    /// <summary>
    /// Disables every anomaly without any transition. Guarantees a clean suspect regardless
    /// of prior state.
    /// </summary>
    public void InitializeClean()
    {
        ClearInitializationState(deactivateActive: true);

        foreach (Anomaly anomaly in CollectAllAnomalies())
            InitializeDisabled(anomaly);

        Debug.Log("[AnomalyController] Suspect forced clean — all anomalies disabled.");
    }

    /// <summary>
    /// Builds a deterministic network snapshot of the anomaly state selected on the server.
    /// Arrays are used directly as ClientRpc parameters to avoid relying on per-anomaly RPC ordering.
    /// </summary>
    public void BuildSnapshot(
        out int[] activeAnomalyIds,
        out int[] disabledAnomalyIds,
        out int[] tentacleAnomalyIds,
        out int[] tentacleCounts,
        out int[] tentacleFlatIndices,
        out int[] tumorAnomalyIds,
        out int[] tumorCounts,
        out int[] tumorFlatIndices)
    {
        activeAnomalyIds = activeAnomalies
            .Where(anomaly => anomaly != null)
            .Select(GetAnomalyId)
            .Where(id => id >= 0)
            .ToArray();

        disabledAnomalyIds = DisabledAnomalyIds.ToArray();

        BuildFlattenedIndexSnapshot(
            TentacleAnomalyIndices,
            out tentacleAnomalyIds,
            out tentacleCounts,
            out tentacleFlatIndices);

        BuildFlattenedIndexSnapshot(
            TumorAnomalyIndices,
            out tumorAnomalyIds,
            out tumorCounts,
            out tumorFlatIndices);
    }

    /// <summary>
    /// Applies the server-selected anomaly state on a client. Dependencies must be injected before this method is called.
    /// </summary>
    public void ApplySnapshot(
        int[] activeAnomalyIds,
        int[] disabledAnomalyIds,
        int[] tentacleAnomalyIds,
        int[] tentacleCounts,
        int[] tentacleFlatIndices,
        int[] tumorAnomalyIds,
        int[] tumorCounts,
        int[] tumorFlatIndices)
    {
        ResetLocalAnomalyState();
        ClearInitializationState();

        if (disabledAnomalyIds != null)
        {
            foreach (int anomalyId in disabledAnomalyIds)
            {
                Anomaly anomaly = FindAnomalyById(anomalyId);
                if (anomaly == null)
                {
                    Debug.LogWarning($"[AnomalyController] Snapshot disabled anomaly not found at anomaly id {anomalyId}.", this);
                    continue;
                }

                anomaly.InitializeDisabled();
                DisabledAnomalyIds.Add(anomalyId);
            }
        }

        if (activeAnomalyIds == null)
            return;

        foreach (int anomalyId in activeAnomalyIds)
        {
            Anomaly anomaly = FindAnomalyById(anomalyId);
            if (anomaly == null)
            {
                Debug.LogWarning($"[AnomalyController] Snapshot active anomaly not found at anomaly id {anomalyId}.", this);
                continue;
            }

            ApplyActiveAnomalyFromSnapshot(
                anomaly,
                tentacleAnomalyIds,
                tentacleCounts,
                tentacleFlatIndices,
                tumorAnomalyIds,
                tumorCounts,
                tumorFlatIndices);
        }
    }

    private void ResetLocalAnomalyState()
    {
        foreach (Anomaly anomaly in activeAnomalies.ToArray())
        {
            if (anomaly != null)
                anomaly.DeactivateAnomaly();
        }
    }

    private void ClearInitializationState(bool deactivateActive = false)
    {
        if (deactivateActive)
            ResetLocalAnomalyState();

        DisabledAnomalyIds.Clear();
        activeAnomalies.Clear();
        TentacleAnomalyIndices.Clear();
        TumorAnomalyIndices.Clear();
    }

    private void ApplyActiveAnomalyFromSnapshot(
        Anomaly anomaly,
        int[] tentacleAnomalyIds,
        int[] tentacleCounts,
        int[] tentacleFlatIndices,
        int[] tumorAnomalyIds,
        int[] tumorCounts,
        int[] tumorFlatIndices)
    {
        if (anomaly == null || activeAnomalies.Contains(anomaly))
            return;

        int anomalyId = GetAnomalyId(anomaly);
        if (anomalyId < 0)
            return;

        activeAnomalies.Add(anomaly);

        if (anomaly is RandomTentacleAnomaly tentacleAnomaly)
        {
            int[] indices = TryGetFlattenedIndices(anomalyId, tentacleAnomalyIds, tentacleCounts, tentacleFlatIndices);
            TentacleAnomalyIndices[anomalyId] = indices;
            tentacleAnomaly.ActivateWithIndices(indices);
            return;
        }

        if (anomaly is RandomTumorAnomaly tumorAnomaly)
        {
            int[] indices = TryGetFlattenedIndices(anomalyId, tumorAnomalyIds, tumorCounts, tumorFlatIndices);
            TumorAnomalyIndices[anomalyId] = indices;
            tumorAnomaly.ActivateWithIndices(indices);
            return;
        }

        anomaly.ActivateAnomaly();
    }

    private Anomaly FindAnomalyById(int anomalyId)
    {
        Anomaly[] allAnomalies = CollectAllAnomalies();
        if (anomalyId < 0 || anomalyId >= allAnomalies.Length)
            return null;

        return allAnomalies[anomalyId];
    }

    private int GetAnomalyId(Anomaly anomaly)
    {
        if (anomaly == null)
            return -1;

        Anomaly[] allAnomalies = CollectAllAnomalies();
        for (int i = 0; i < allAnomalies.Length; i++)
        {
            if (ReferenceEquals(allAnomalies[i], anomaly))
                return i;
        }

        Debug.LogWarning($"[AnomalyController] Anomaly '{anomaly.name}' is not present in serialized anomaly lists.", this);
        return -1;
    }

    private static void BuildFlattenedIndexSnapshot(
        Dictionary<int, int[]> source,
        out int[] anomalyIds,
        out int[] counts,
        out int[] flatIndices)
    {
        if (source == null || source.Count == 0)
        {
            anomalyIds = System.Array.Empty<int>();
            counts = System.Array.Empty<int>();
            flatIndices = System.Array.Empty<int>();
            return;
        }

        var ordered = source.OrderBy(kvp => kvp.Key).ToArray();
        anomalyIds = new int[ordered.Length];
        counts = new int[ordered.Length];

        int totalCount = 0;
        for (int i = 0; i < ordered.Length; i++)
        {
            anomalyIds[i] = ordered[i].Key;
            counts[i] = ordered[i].Value?.Length ?? 0;
            totalCount += counts[i];
        }

        flatIndices = new int[totalCount];
        int writeIndex = 0;
        foreach (var kvp in ordered)
        {
            if (kvp.Value == null)
                continue;

            foreach (int index in kvp.Value)
                flatIndices[writeIndex++] = index;
        }
    }

    private static int[] TryGetFlattenedIndices(int anomalyId, int[] anomalyIds, int[] counts, int[] flatIndices)
    {
        if (anomalyIds == null || counts == null || flatIndices == null)
            return System.Array.Empty<int>();

        int flatOffset = 0;
        for (int i = 0; i < anomalyIds.Length && i < counts.Length; i++)
        {
            int count = Mathf.Max(0, counts[i]);
            if (anomalyIds[i] == anomalyId)
            {
                int safeCount = Mathf.Min(count, Mathf.Max(0, flatIndices.Length - flatOffset));
                int[] result = new int[safeCount];
                System.Array.Copy(flatIndices, flatOffset, result, 0, safeCount);
                return result;
            }

            flatOffset += count;
        }

        return System.Array.Empty<int>();
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
    /// category class named <paramref name="categoryTypeName"/> (e.g. "PhysicalAnomaly").
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
    /// Returns a new list containing only the anomalies whose C# type name is currently
    /// unlocked according to <see cref="AnomalyUnlockManager"/>. Anomalies that are filtered
    /// out are immediately passed to <see cref="InitializeDisabled"/> so they are visually
    /// reset and their anomaly ids are recorded for client replication.
    /// When <see cref="AnomalyUnlockManager.Instance"/> is null (e.g. during tests), the
    /// full pool is returned unchanged.
    /// </summary>
    private List<Anomaly> FilterToUnlocked(List<Anomaly> pool)
    {
        AnomalyUnlockManager unlockManager = AnomalyUnlockManager.Instance;
        if (unlockManager == null) return pool;

        var unlocked = new List<Anomaly>(pool.Count);
        foreach (Anomaly a in pool)
        {
            if (a == null) continue;

            if (unlockManager.IsAnomalyUnlocked(a.GetType().Name))
                unlocked.Add(a);
            else
                InitializeDisabled(a);
        }
        return unlocked;
    }

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

    // ── Debug API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all anomaly components across all five category lists.
    /// Intended for debug / editor tooling only — not for gameplay logic.
    /// </summary>
    public Anomaly[] GetAllAnomaliesDebug() => CollectAllAnomalies();

    /// <summary>
    /// Toggles a single anomaly on or off for in-editor debug purposes.
    /// Does not issue any network RPCs — local only.
    /// </summary>
    public void DebugToggleAnomaly(Anomaly anomaly)
    {
        if (anomaly == null) return;

        if (activeAnomalies.Contains(anomaly))
        {
            anomaly.DeactivateAnomaly();
            activeAnomalies.Remove(anomaly);
            Debug.Log($"[AnomalyController] Debug: deactivated '{anomaly.GetType().Name}'.");
        }
        else
        {
            ActivateAnomaly(anomaly);
            Debug.Log($"[AnomalyController] Debug: activated '{anomaly.GetType().Name}'.");
        }
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
    /// Returns the anomaly id of the removed anomaly for client replication,
    /// or -1 if there are no active anomalies.
    /// </summary>
    public int RemoveRandomActiveAnomaly()
    {
        if (activeAnomalies.Count == 0) return -1;

        int index = Random.Range(0, activeAnomalies.Count);
        Anomaly anomaly = activeAnomalies[index];
        int anomalyId = GetAnomalyId(anomaly);

        anomaly.DeactivateAnomaly();
        activeAnomalies.RemoveAt(index);

        Debug.Log($"[AnomalyController] Vaccine applied — deactivated '{anomaly.name}' (anomalyId {anomalyId}). " +
                  $"{activeAnomalies.Count} anomaly/ies remaining.");
        return anomalyId;
    }

    /// <summary>
    /// Deactivates and removes the anomaly with the given deterministic anomaly id.
    /// Used on non-server clients to replicate a server-chosen anomaly removal.
    /// </summary>
    public void RemoveAnomalyById(int anomalyId)
    {
        Anomaly target = FindAnomalyById(anomalyId);

        if (target == null)
        {
            Debug.LogWarning($"[AnomalyController] RemoveAnomalyById: no anomaly at anomaly id {anomalyId}.");
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
            int anomalyId = GetAnomalyId(anomaly);
            int[] indices = tentacleAnomaly.PickActiveIndices();
            if (anomalyId >= 0)
                TentacleAnomalyIndices[anomalyId] = indices;
            tentacleAnomaly.ActivateWithIndices(indices);
        }
        else if (anomaly is RandomTumorAnomaly tumorAnomaly)
        {
            int anomalyId = GetAnomalyId(anomaly);
            int[] indices = tumorAnomaly.PickActiveIndices();
            if (anomalyId >= 0)
                TumorAnomalyIndices[anomalyId] = indices;
            tumorAnomaly.ActivateWithIndices(indices);
        }
        else
        {
            anomaly.ActivateAnomaly();
        }
    }

    /// <summary>
    /// Calls InitializeDisabled on an anomaly and records its anomaly id for client replication.
    /// </summary>
    private void InitializeDisabled(Anomaly anomaly)
    {
        anomaly.InitializeDisabled();

        int anomalyId = GetAnomalyId(anomaly);
        if (anomalyId >= 0)
            DisabledAnomalyIds.Add(anomalyId);
    }
}
