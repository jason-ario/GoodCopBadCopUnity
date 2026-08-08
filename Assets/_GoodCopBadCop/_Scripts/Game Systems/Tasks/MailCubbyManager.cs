using System.Collections.Generic;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Placed on the "Mail Cubby manager" GameObject. Randomly picks which of the physical
/// <see cref="MailCubbySlot"/> cubbies (assigned via <see cref="_mailCubbySlots"/>) are enabled —
/// exactly one per eligible resident in <see cref="_suspectPool"/> — and assigns each enabled
/// cubby a random resident, updating its tape label to match (see
/// <see cref="MailCubbySlot.SetAssignedResident"/>). Any cubby not picked is deactivated so there
/// are never more active cubbies than residents to fill them.
///
/// The slots are no longer nested under this manager's GameObject: each <see cref="MailCubbySlot"/>
/// carries its own <see cref="NetworkObject"/>, and Netcode's NetworkObject hierarchy only
/// supports one level of NetworkObject parenting (no NetworkObject grandchildren). They must
/// instead be manually wired up via <see cref="_mailCubbySlots"/>.
///
/// Server-authoritative: the random shuffle only ever runs on the server. The resulting
/// active/inactive flags and resident assignments (as indices into <see cref="_suspectPool"/>,
/// which is the same static asset on every build) are replicated to every client via
/// <see cref="ApplyAssignmentClientRpc"/> so every peer's <see cref="MailCubbySlot"/> ends up with
/// the exact same active state and tape label as the server — this is required because
/// <see cref="MailCubbySlot.HandleItemPlaced"/> reads its own locally-assigned resident name and
/// sends it up via <see cref="MailPackageItem.RequestSortServerRpc"/> for the server to validate.
///
/// Setup:
///   - Requires a <see cref="NetworkObject"/> component on the same GameObject (scene object).
///   - Assign <see cref="_suspectPool"/> to the <see cref="SuspectSet"/> asset residents should be
///     drawn from (e.g. "All Suspects").
///   - Assign every <see cref="MailCubbySlot"/> this manager should control to
///     <see cref="_mailCubbySlots"/> (they live as sibling top-level objects, not children).
///   - Call <see cref="AutoAssignRandomResidents"/> (right-click the component in the Inspector —
///     works in Edit Mode too, applying directly to the scene with no networking involved — or
///     leave <see cref="_assignOnDayStart"/> enabled to run it automatically every day at day
///     start, on <see cref="CampaignManager.OnDayChanged"/>, server-only, while playing).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MailCubbyManager : NetworkBehaviour
{
    [Tooltip("The pool of suspects cubbies can be randomly assigned to. Must be the same asset on every build — assignments are replicated as indices into this list.")]
    [SerializeField] private SuspectSet _suspectPool;

    [Tooltip("If true, only suspects with IsResident set are eligible for a cubby assignment.")]
    [SerializeField] private bool _residentsOnly = true;

    [Tooltip("If true, automatically randomizes which cubbies are enabled and their assignments every day, on CampaignManager.OnDayChanged (server-only). Replaces the old assign-on-spawn behavior, which could run before other systems (e.g. the resident/suspect pool) were fully ready.")]
    [SerializeField] private bool _assignOnDayStart = true;

    // Server-only cache of the last assignment broadcast, so late-joining clients (who connect
    // after AutoAssignRandomResidents already ran and its ClientRpc already went out) can be
    // brought up to date individually instead of being stuck with whatever _assignedResident/
    // active-state came baked into the prefab. Without this, any player who joins after the
    // initial shuffle never receives the assignment and sees blank/mismatched cubby labels —
    // this is the "names not synced across clients" bug.
    private bool[] _lastActiveFlags;
    private int[] _lastResidentAssignment;
    private bool _hasAssignment;

    [Tooltip("The physical MailCubbySlot cubbies this manager controls. Must be assigned manually — " +
        "the slots are their own top-level NetworkObjects (not children of this manager), since " +
        "Netcode's NetworkObject hierarchy only supports one level of nesting (no NetworkObject " +
        "grandchildren). Order does not matter.")]
    [SerializeField] private MailCubbySlot[] _mailCubbySlots;

    /// <summary>Scene-wide singleton, mirroring <see cref="SortMailTask.Instance"/> — there is only ever one Mail Cubby manager in the scene.</summary>
    public static MailCubbyManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[MailCubbyManager] Duplicate instance detected — destroying self.", this);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            CampaignManager.OnDayChanged += OnDayChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            CampaignManager.OnDayChanged -= OnDayChanged;
        }

        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only. Re-shuffles the cubby layout every day (if <see cref="_assignOnDayStart"/> is
    /// enabled), so residents are reassigned to fresh cubbies at day start rather than once on
    /// spawn — see <see cref="_assignOnDayStart"/>.
    /// </summary>
    private void OnDayChanged(int day)
    {
        if (_assignOnDayStart)
            AutoAssignRandomResidents();
    }

    /// <summary>
    /// Server-only. Turns on the outline highlight on every physical "Mail Cubbies" stand (the
    /// <see cref="HighlightEffect"/> lives on each stand's root GameObject, not on the individual
    /// <see cref="MailCubbySlot"/> cubbies) on every client. Intended to be called right after a
    /// delivery arrives (see <see cref="DeliveryTruckController"/>) so players can immediately spot
    /// where to go — cleared again as soon as any package is dropped into any cubby, via
    /// <see cref="ClearAllHighlights"/> (see <see cref="SortMailTask.EvaluateSort"/>).
    /// </summary>
    public void HighlightAllActiveCubbies()
    {
        if (!IsServer) return;
        SetHighlightClientRpc(true);
    }

    /// <summary>
    /// Server-only. Turns off the outline highlight on every "Mail Cubbies" stand root on every
    /// client. Safe to call even if nothing is currently highlighted.
    /// </summary>
    public void ClearAllHighlights()
    {
        if (!IsServer) return;
        SetHighlightClientRpc(false);
    }

    [ClientRpc]
    private void SetHighlightClientRpc(bool highlight)
    {
        // The outline highlight lives on each "Mail Cubbies" stand's root GameObject (see the
        // "Mail Cubbies" prefab), not on the individual MailCubbySlot cubbies — this points the
        // player at the whole stand rather than calling out every slot separately.
        HighlightEffect[] cubbyHighlights = GetComponentsInChildren<HighlightEffect>(true);
        foreach (HighlightEffect fx in cubbyHighlights)
        {
            fx.enabled = true;
            fx.highlighted = highlight;
        }
    }

    /// <summary>
    /// Server-only. Replays the last broadcast cubby assignment to a single newly-connected
    /// client so late joiners end up with the exact same layout as everyone else, instead of the
    /// prefab-default (unassigned) state their <see cref="MailCubbySlot"/> instances start with.
    /// </summary>
    private void OnClientConnected(ulong clientId)
    {
        if (!_hasAssignment) return;

        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        ApplyAssignmentClientRpc(_lastActiveFlags, _lastResidentAssignment, rpcParams);
    }

    /// <summary>
    /// Returns the cubby slots this manager controls. Prefers the manually-assigned
    /// <see cref="_mailCubbySlots"/> array (required now that slots are separate top-level
    /// NetworkObjects and can no longer be discovered via <c>GetComponentsInChildren</c>). Falls
    /// back to a children search for any legacy setup that hasn't been migrated yet, with a
    /// warning so it gets caught and fixed.
    /// </summary>
    private MailCubbySlot[] GetSlots()
    {
        if (_mailCubbySlots != null && _mailCubbySlots.Length > 0)
            return _mailCubbySlots;

        Debug.LogWarning("[MailCubbyManager] _mailCubbySlots is not assigned — falling back to " +
            "GetComponentsInChildren, which will find nothing now that slots are separate " +
            "top-level NetworkObjects. Assign _mailCubbySlots in the inspector.", this);
        return GetComponentsInChildren<MailCubbySlot>(true);
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
        // In Play Mode this must be server-only — cubby assignments are authoritative and
        // replicated to all clients via ApplyAssignmentClientRpc. In the Editor (not playing),
        // there is no server/NetworkObject spawned yet (IsServer is meaningless), so the context
        // menu entry is meant to run entirely locally instead — see EditorAssignRandomResidents,
        // which applies the same shuffle directly to the scene without touching networking.
        if (Application.isPlaying && !IsServer)
        {
            Debug.LogWarning("[MailCubbyManager] AutoAssignRandomResidents is server-only in Play Mode — cubby assignments must be authoritative and replicated to all clients.", this);
            return;
        }

        MailCubbySlot[] allSlots = GetSlots();
        if (allSlots.Length == 0)
        {
            Debug.LogWarning("[MailCubbyManager] No MailCubbySlot assigned — nothing to assign.", this);
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

        if (!Application.isPlaying)
        {
            // Editor-only run (see the doc comment above): nothing is networked yet, so just
            // apply locally to the scene objects — mark them dirty so the change is saved.
#if UNITY_EDITOR
            foreach (MailCubbySlot slot in allSlots)
            {
                if (slot != null)
                    UnityEditor.EditorUtility.SetDirty(slot);
            }
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            Debug.Log($"[MailCubbyManager] (Editor) Randomly enabled {activeCount}/{allSlots.Length} cubby slot(s) and assigned residents from a pool of {residentPoolIndices.Count}.");
            return;
        }

        // Cache so late-joining clients can be caught up individually via OnClientConnected.
        _lastActiveFlags = activeFlags;
        _lastResidentAssignment = residentAssignment;
        _hasAssignment = true;

        ApplyAssignmentClientRpc(activeFlags, residentAssignment);

        Debug.Log($"[MailCubbyManager] Randomly enabled {activeCount}/{allSlots.Length} cubby slot(s) and assigned residents from a pool of {residentPoolIndices.Count}, replicated to all clients.");
    }

    /// <summary>
    /// Replicates the server's cubby layout to a client (including the host, which skips this
    /// since it already applied the layout locally). Indices refer to <see cref="_suspectPool"/>,
    /// which every client shares as the same static asset, so no object references need to cross
    /// the network. Defaults to broadcasting to everyone, but <see cref="OnClientConnected"/> also
    /// targets this at a single late-joining client via <paramref name="rpcParams"/>.
    /// </summary>
    [ClientRpc]
    private void ApplyAssignmentClientRpc(bool[] activeFlags, int[] residentAssignment, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return;

        MailCubbySlot[] allSlots = GetSlots();
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

    /// <summary>Returns the index of <paramref name="resident"/> within <see cref="_suspectPool"/>.suspects, or -1 if not found/null. Used to transmit a SuspectData reference across an RPC as a cheap index instead of a string — see <see cref="MailCubbySlot.ResidentPoolIndex"/> and <see cref="SortMailTask.EvaluateSort"/>, which resolves it back via <see cref="ResolveResident"/> and compares the actual SuspectData reference directly.</summary>
    public int GetResidentIndex(SuspectData resident)
    {
        if (resident == null || _suspectPool == null || _suspectPool.suspects == null) return -1;
        return _suspectPool.suspects.IndexOf(resident);
    }

    /// <summary>Resolves an index (as returned by <see cref="GetResidentIndex"/>) back to the actual <see cref="SuspectData"/> asset reference, or null if out of range.</summary>
    public SuspectData ResolveResident(int index)
    {
        if (_suspectPool == null || _suspectPool.suspects == null) return null;
        if (index < 0 || index >= _suspectPool.suspects.Count) return null;
        return _suspectPool.suspects[index];
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
