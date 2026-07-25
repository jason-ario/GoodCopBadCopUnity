using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Placed on the "Mail Cubby manager" GameObject. Randomly picks which of the physical
/// <see cref="MailCubbySlot"/> cubbies (found in children) are enabled — exactly one per eligible
/// resident in <see cref="_suspectPool"/> — and assigns each enabled cubby a random resident,
/// updating its tape label to match (see <see cref="MailCubbySlot.SetAssignedResident"/>). Any
/// cubby not picked is deactivated so there are never more active cubbies than residents to fill
/// them.
///
/// Setup:
///   - Assign <see cref="_suspectPool"/> to the <see cref="SuspectSet"/> asset residents should be
///     drawn from (e.g. "All Suspects").
///   - Call <see cref="AutoAssignRandomResidents"/> (right-click the component in the Inspector, or
///     leave <see cref="_assignOnAwake"/> enabled to run it automatically when the scene loads).
/// </summary>
public class MailCubbyManager : MonoBehaviour
{
    [Tooltip("The pool of suspects cubbies can be randomly assigned to.")]
    [SerializeField] private SuspectSet _suspectPool;

    [Tooltip("If true, only suspects with IsResident set are eligible for a cubby assignment.")]
    [SerializeField] private bool _residentsOnly = true;

    [Tooltip("If true, automatically randomizes which cubbies are enabled and their assignments once when this component wakes up.")]
    [SerializeField] private bool _assignOnAwake = false;

    private void Awake()
    {
        if (_assignOnAwake)
            AutoAssignRandomResidents();
    }

    /// <summary>
    /// Finds every <see cref="MailCubbySlot"/> under this manager (active or not), shuffles them,
    /// and enables a random subset — one per eligible resident in <see cref="_suspectPool"/> — while
    /// deactivating the rest. Each enabled cubby is then assigned a distinct random resident drawn
    /// from a separately shuffled resident pool, so both which cubbies are used and who is assigned
    /// to each one change every time this runs.
    /// </summary>
    [ContextMenu("Auto-Assign Random Residents")]
    public void AutoAssignRandomResidents()
    {
        MailCubbySlot[] allSlots = GetComponentsInChildren<MailCubbySlot>(true);
        if (allSlots.Length == 0)
        {
            Debug.LogWarning("[MailCubbyManager] No MailCubbySlot found in children — nothing to assign.", this);
            return;
        }

        List<SuspectData> pool = BuildResidentPool();
        if (pool.Count == 0)
        {
            Debug.LogWarning("[MailCubbyManager] Suspect pool is empty — cannot assign cubbies.", this);
            return;
        }

        List<MailCubbySlot> shuffledSlots = new List<MailCubbySlot>(allSlots);
        Shuffle(shuffledSlots);

        int activeCount = Mathf.Min(pool.Count, shuffledSlots.Count);
        Shuffle(pool);

        for (int i = 0; i < shuffledSlots.Count; i++)
        {
            MailCubbySlot slot = shuffledSlots[i];
            bool shouldBeActive = i < activeCount;
            slot.gameObject.SetActive(shouldBeActive);

            if (shouldBeActive)
                slot.SetAssignedResident(pool[i]);
        }

        Debug.Log($"[MailCubbyManager] Randomly enabled {activeCount}/{allSlots.Length} cubby slot(s) and assigned residents from a pool of {pool.Count}.");
    }

    private List<SuspectData> BuildResidentPool()
    {
        var pool = new List<SuspectData>();
        if (_suspectPool == null || _suspectPool.suspects == null) return pool;

        foreach (SuspectData suspect in _suspectPool.suspects)
        {
            if (suspect == null) continue;
            if (_residentsOnly && !suspect.IsResident) continue;
            pool.Add(suspect);
        }

        return pool;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
