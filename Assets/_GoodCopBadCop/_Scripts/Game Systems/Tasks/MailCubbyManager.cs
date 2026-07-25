using System.Collections.Generic;
using Unity.Netcode;
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
/// Server-authoritative: the random shuffle only ever runs on the server. The resulting
/// active/inactive flags and resident assignments (as indices into <see cref="_suspectPool"/>,
/// which is the same static asset on every build) are replicated to every client via
/// <see cref="ApplyAssignmentClientRpc"/> so every peer's <see cref="MailCubbySlot"/> ends up with
/// the exact same active state and tape label as the server — this is required because
/// <see cref="MailCubbySlot.OnTriggerEnter"/> reads its own locally-assigned resident name and
/// sends it up via <see cref="MailPackageItem.RequestSortServerRpc"/> for the server to validate.
///
/// Setup:
///   - Requires a <see cref="NetworkObject"/> component on the same GameObject (scene object).
///   - Assign <see cref="_suspectPool"/> to the <see cref="SuspectSet"/> asset residents should be
///     drawn from (e.g. "All Suspects").
///   - Call <see cref="AutoAssignRandomResidents"/> (right-click the component in the Inspector —
///     server/host only — or leave <see cref="_assignOnAwake"/> enabled to run it automatically
///     once this NetworkObject spawns on the server).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MailCubbyManager : NetworkBehaviour
{
    [Tooltip("The pool of suspects cubbies can be randomly assigned to. Must be the same asset on every build — assignments are replicated as indices into this list.")]
    [SerializeField] private SuspectSet _suspectPool;

    [Tooltip("If true, only suspects with IsResident set are eligible for a cubby assignment.")]
    [SerializeField] private bool _residentsOnly = true;

    [Tooltip("If true, automatically randomizes which cubbies are enabled and their assignments once when this NetworkObject spawns on the server.")]
    [SerializeField] private bool _assignOnAwake = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _assignOnAwake)
            AutoAssignRandomResidents();
    }

    /// <summary>
    /// Server-only. Finds every <see cref="MailCubbySlot"/> under this manager (active or not),
    /// shuffles them, and enables a random subset — one per eligible resident in
    /// <see cref="_suspectPool"/> — while deactivating the rest. Each enabled cubby is then
    /// assigned a distinct random resident drawn from a separately shuffled resident pool, so both
    /// which cubbies are used and who is assigned to each one change every time this runs. The
    /// resulting layout is applied locally and then replicated to every client.
    /// </summary>
    [ContextMenu("Auto-Assign Random Residents")]
    public void AutoAssignRandomResidents()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[MailCubbyManager] AutoAssignRandomResidents is server-only — cubby assignments must be authoritative and replicated to all clients.", this);
            return;
        }

        MailCubbySlot[] allSlots = GetComponentsInChildren<MailCubbySlot>(true);
        if (allSlots.Length == 0)
        {
            Debug.LogWarning("[MailCubbyManager] No MailCubbySlot found in children — nothing to assign.", this);
            return;
        }

        List<int> residentPoolIndices = BuildResidentPoolIndices();
        if (residentPoolIndices.Count == 0)
        {
            Debug.LogWarning("[MailCubbyManager] Suspect pool is empty — cannot assign cubbies.", this);
            return;
        }

        List<int> slotOrder = new List<int>(allSlots.Length);
        for (int i = 0; i < allSlots.Length; i++)
            slotOrder.Add(i);
        Shuffle(slotOrder);

        int activeCount = Mathf.Min(residentPoolIndices.Count, allSlots.Length);
        Shuffle(residentPoolIndices);

        bool[] activeFlags = new bool[allSlots.Length];
        int[] residentAssignment = new int[allSlots.Length];
        for (int i = 0; i < residentAssignment.Length; i++)
            residentAssignment[i] = -1;

        for (int i = 0; i < slotOrder.Count; i++)
        {
            int slotIndex = slotOrder[i];
            bool shouldBeActive = i < activeCount;
            activeFlags[slotIndex] = shouldBeActive;

            if (shouldBeActive)
                residentAssignment[slotIndex] = residentPoolIndices[i];
        }

        ApplyAssignment(allSlots, activeFlags, residentAssignment);
        ApplyAssignmentClientRpc(activeFlags, residentAssignment);

        Debug.Log($"[MailCubbyManager] Randomly enabled {activeCount}/{allSlots.Length} cubby slot(s) and assigned residents from a pool of {residentPoolIndices.Count}, replicated to all clients.");
    }

    /// <summary>
    /// Replicates the server's cubby layout to every client (including the host, which skips this
    /// since it already applied the layout locally). Indices refer to <see cref="_suspectPool"/>,
    /// which every client shares as the same static asset, so no object references need to cross
    /// the network.
    /// </summary>
    [ClientRpc]
    private void ApplyAssignmentClientRpc(bool[] activeFlags, int[] residentAssignment)
    {
        if (IsServer) return;

        MailCubbySlot[] allSlots = GetComponentsInChildren<MailCubbySlot>(true);
        ApplyAssignment(allSlots, activeFlags, residentAssignment);
    }

    private void ApplyAssignment(MailCubbySlot[] allSlots, bool[] activeFlags, int[] residentAssignment)
    {
        int count = Mathf.Min(allSlots.Length, Mathf.Min(activeFlags.Length, residentAssignment.Length));
        for (int i = 0; i < count; i++)
        {
            MailCubbySlot slot = allSlots[i];
            slot.gameObject.SetActive(activeFlags[i]);

            int residentIndex = residentAssignment[i];
            if (activeFlags[i] && residentIndex >= 0 && _suspectPool != null &&
                _suspectPool.suspects != null && residentIndex < _suspectPool.suspects.Count)
            {
                slot.SetAssignedResident(_suspectPool.suspects[residentIndex]);
            }
        }
    }

    /// <summary>Returns indices into <see cref="_suspectPool"/>.suspects for every eligible resident.</summary>
    private List<int> BuildResidentPoolIndices()
    {
        var pool = new List<int>();
        if (_suspectPool == null || _suspectPool.suspects == null) return pool;

        for (int i = 0; i < _suspectPool.suspects.Count; i++)
        {
            SuspectData suspect = _suspectPool.suspects[i];
            if (suspect == null) continue;
            if (_residentsOnly && !suspect.IsResident) continue;
            pool.Add(i);
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
