using System.Collections;
using System.Linq;
using HighlightPlus;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

// LateUpdate must run after PlayerAnimationController (default order 0) and
// PlayerPickupController (order 1) so pages snap to the notebook's final
// world position for the frame — not the position from the previous frame.
[DefaultExecutionOrder(2)]
public class ExamNotebook : PickableObject
{
    // Populated at runtime via SpawnAndWirePages (server) → SetPageReferencesClientRpc (clients).
    // Empty at design time — no page children live in the notebook prefab.
    private ExamPage[] pages = System.Array.Empty<ExamPage>();

    // Guards against double-initialization when both a broadcast SetPageReferencesClientRpc
    // (sent immediately after purchase/delivery) and a targeted one (sent in response to
    // RequestPageReferencesServerRpc) arrive on the same client within the same session.
    private bool _pagesInitialized;

    /// <summary>The registered page prefab asset for this notebook type. Must match the entry in the NetworkManager's Network Prefabs list.</summary>
    [SerializeField] private NetworkObject pagePrefab;

    /// <summary>
    /// An inactive copy of the page prefab parented inside this notebook at design time.
    /// Because it is a child of the notebook it inherits the correct local scale,
    /// so its lossyScale is used as the authoritative world scale for live pages that
    /// follow the notebook via LateUpdate rather than through NGO parenting.
    /// </summary>
    [SerializeField] private Transform pageScaleReference;

    /// <summary>
    /// World-position anchors for each page slot, defined as child Transforms of the notebook.
    /// Page i snaps to pagePositions[i] each LateUpdate while it is still bound to the notebook.
    /// Supports up to 5 slots — one per _pageBitmask field.
    /// </summary>
    [SerializeField] private Transform[] pagePositions = new Transform[5];

    /// <summary>Number of page instances to spawn when this notebook is purchased.</summary>
    [SerializeField] private int pageCount = 1;

    /// <summary>
    /// The anomaly category this notebook represents. Must match an
    /// <see cref="AnomalyUnlockProgressionSO.AnomalyCategoryData.CategoryName"/> entry in
    /// AnomalyUnlockManager's progression asset (e.g. "Documentation", "Physical", "Vitals",
    /// "Behavior", "Supernatural"). Drives automatic checklist population on every page
    /// when the notebook spawns — see <see cref="ExamPage.BuildChecklistFromCategory"/>.
    /// </summary>
    [SerializeField] private string categoryName;
    public string CategoryName => categoryName;

    bool addingToFolder = false;
    public bool IsChecking { get; set; }

    /// <summary>
    /// Fired on the local client whenever any checkbox is toggled on any ExamNotebook.
    /// Subscribe in tutorial scripts to react to checklist interaction without polling.
    /// </summary>
    public static event System.Action<ExamNotebook> OnAnyCheckboxChecked;

    /// <summary>
    /// Fired on all clients immediately after any exam notebook page is placed into a folder.
    /// Subscribe in tutorial scripts to gate progression on the notebook-filing step.
    /// Subscribing <i>before</i> showing tutorial dialogue guarantees the event is not missed
    /// if the player files the page early.
    /// </summary>
    public static event System.Action OnAnyNotebookPageFiled;

    /// <summary>
    /// Set to true on all clients the moment any page is placed into a folder.
    /// Reset this to false at the start of any beat that needs to gate on filing,
    /// then subscribe to <see cref="OnAnyNotebookPageFiled"/> before showing dialogue
    /// so filing that occurs during the prompt is captured via the flag.
    /// </summary>
    public static bool AnyPageFiled;

    /// <summary>
    /// Fired on all clients when any exam notebook is picked up by a player.
    /// Subscribe in tutorial scripts to detect when the player has acquired a checklist.
    /// </summary>
    public static event System.Action OnAnyExamNotebookPickedUp;

    /// <summary>
    /// Set to true on all clients the moment any exam notebook is picked up.
    /// Reset this to false before starting any beat that gates on the player acquiring
    /// a checklist, so an early pickup is still captured via the flag.
    /// </summary>
    public static bool AnyExamNotebookPickedUp;

    /// <summary>
    /// Returns true when every visible checklist item on the current page is checked.
    /// Use as a <c>WaitUntil</c> condition in tutorial coroutines.
    /// </summary>
    public bool AllVisibleBoxesChecked
    {
        get
        {
            if (pages == null || pages.Length == 0 || currentPage >= pages.Length) return false;
            ExamPage page = pages[currentPage];
            if (page == null) return false;
            foreach (ChecklistItem item in page.ChecklistItems)
            {
                if (item == null) continue;
                if (!item.gameObject.activeInHierarchy) continue;
                if (!item.IsChecked) return false;
            }
            return true;
        }
    }

    private int currentPage = 0;

    // Synced so any client that picks up the notebook after a page has been ripped out
    // knows which page is now active and can enable its checklist correctly.
    private NetworkVariable<int> _currentPage = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // One bitmask per page slot — bit N = checklist item N is checked.
    // Declared as fields so NGO registers and syncs them from the notebook's NetworkObject,
    // which is the only properly spawned NetworkObject in this hierarchy.
    // Up to 5 pages per notebook; each supports up to 32 checklist items.
    private NetworkVariable<int> _pageBitmask0 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _pageBitmask1 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _pageBitmask2 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _pageBitmask3 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _pageBitmask4 = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int>[] _pageBitmasks;
    [SerializeField] private AudioClip ripOutSound;

    // ── Controller checklist navigation ─────────────────────────────────────────
    /// <summary>True while the "draw mode" checklist view is open (see OnStartUse/ExitDrawMode).</summary>
    private bool _isInDrawMode;

    /// <summary>The checklist item currently highlighted by controller navigation, if any.</summary>
    private ChecklistItem _controllerSelectedItem;

    /// <summary>Earliest time the left-stick navigation can fire again (debounce).</summary>
    private float _stickNavTime;

    private const float ChecklistStickNavCooldown = 0.25f;
    // ─────────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        // pages[] is empty at design time — nothing to iterate here.
        // It gets populated at runtime via SpawnAndWirePages → SetPageReferencesClientRpc.
        ExcludePageCollidersFromCache();

        foreach (Transform slot in pagePositions)
        {
            if (slot != null)
                slot.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart += OnDayStarted;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnDayStart -= OnDayStarted;
    }

    /// <summary>
    /// Re-renders all page RTs at the start of each day so the checklist view is never blank
    /// when the player first opens the notebook during a shift.
    /// </summary>
    private void OnDayStarted()
    {
        SnapshotAllPages();
    }

    /// <summary>
    /// Rebuilds the notebook's InteractableCollider cache excluding any colliders that belong
    /// to a page. Pages manage their own collider state — the notebook must not touch them.
    /// Call this after Awake and again after a page is ripped out.
    /// </summary>
    private void ExcludePageCollidersFromCache()
    {
        var pageColliderSet = new System.Collections.Generic.HashSet<InteractableCollider>();
        foreach (var page in pages)
        {
            if (page == null) continue;
            foreach (var ic in page.GetComponentsInChildren<InteractableCollider>(true))
                pageColliderSet.Add(ic);
        }

        var all = GetComponentsInChildren<InteractableCollider>(true);
        var notebookOnly = System.Array.FindAll(all, ic => !pageColliderSet.Contains(ic));
        OverrideInteractableColliders(notebookOnly);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && NetworkObject.IsSceneObject == true)
        {
            // Server: spawn pages and broadcast references to any clients that are already
            // connected. Clients that join after this point will request references themselves
            // via RequestPageReferencesServerRpc → SetPageReferencesClientRpc (targeted).
            var spawnedPages = SpawnAndWirePages();
            if (spawnedPages.Count > 0)
            {
                var pageRefs = new NetworkObjectReference[spawnedPages.Count];
                for (int i = 0; i < spawnedPages.Count; i++)
                    pageRefs[i] = new NetworkObjectReference(spawnedPages[i]);
                SetPageReferencesClientRpc(pageRefs);
            }
        }
        else if (!IsServer)
        {
            // Non-host client: covers both scene objects and dynamically-spawned notebooks.
            //
            // Scene objects: SetPageReferencesClientRpc was broadcast during the server's
            // OnNetworkSpawn, which runs before clients finish connecting — so the RPC is
            // never received. Request page references from the server now.
            //
            // Dynamically-spawned notebooks (purchased / supply-box delivered): a broadcast
            // SetPageReferencesClientRpc is sent by the caller of SpawnAndWirePages right
            // after spawning. If a client joined before the notebook was spawned, that
            // broadcast reaches them. If a client joins AFTER the notebook was already in
            // the world, they missed the broadcast and must request references explicitly.
            //
            // In the early-join case the server will still have pages.Length == 0 when this
            // ServerRpc arrives (purchase hasn't completed yet), so it returns early and the
            // in-flight broadcast RPC handles initialization instead — no double-init.
            RequestPageReferencesServerRpc();
        }

        // Subscribe so every client re-activates pages and re-renders RTs when any player
        // picks up this notebook (critical for notebooks delivered inside supply boxes whose
        // pages were deactivated at delivery time and whose RTs were never rendered).
        OnPickedUpNetworked += OnPickedUpAllClients;

        // Subscribe so every client hides/shows the notebook's dynamically-spawned pages in
        // sync with the notebook itself being stowed/unstowed. The notebook's own GameObject
        // is deactivated on stow via the base class's OnStowedNetworked handling, but the
        // pages are separate NetworkObjects and would otherwise stay visible, floating at
        // the stow point, for every client (including the owner).
        OnStowedNetworked += OnStowedAllClients;
    }

    /// <summary>
    /// Fires on ALL clients via <see cref="PickableObject.OnStowedNetworked"/> whenever this
    /// notebook's authoritative stowed state changes, keeping its pages' visibility in sync
    /// with the notebook across every machine.
    /// </summary>
    private void OnStowedAllClients(bool stowed)
    {
        SetPagesActive(!stowed);
        if (!stowed)
            SnapshotAllPages();
    }

    /// <summary>
    /// Despawns this notebook's dynamically-spawned pages on the server before the notebook
    /// itself is despawned (e.g. thrown in the trash). Pages are separate NetworkObjects, not
    /// children of the notebook, so the default despawn would otherwise leave them orphaned
    /// in the scene for every client. Only despawns pages still actually bound to this
    /// notebook — pages that have been ripped out or filed into a folder are independent
    /// objects at that point and must survive the notebook's destruction.
    /// </summary>
    protected override void OnBeforeDespawnServer()
    {
        base.OnBeforeDespawnServer();

        if (pages == null) return;

        foreach (var page in pages)
        {
            if (page == null) continue;
            // insideThisFolder is set server-side by RequestAddToFolderServerRpc → AddToFolder,
            // so it's reliable here even though it's not itself a NetworkVariable.
            if (page.isRippedOut || page.insideThisFolder != null) continue;

            NetworkHelper.Despawn(page.NetworkObject);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        OnPickedUpNetworked -= OnPickedUpAllClients;
        OnStowedNetworked   -= OnStowedAllClients;
    }

    /// <summary>
    /// Fires on ALL clients via the <see cref="OnPickedUpNetworked"/> event (driven by the
    /// server-authoritative _holdingClientId NetworkVariable) whenever any player picks up
    /// this notebook. Re-activates pages and re-renders their RTs so every client that has
    /// not yet seen a rendered snapshot (e.g. supply-box delivery path) gets correct visuals.
    /// </summary>
    private void OnPickedUpAllClients()
    {
        SetPagesActive(true);
        SnapshotAllPages();
    }

    /// <summary>Coroutine handle for <see cref="SnapshotAllPagesRoutine"/>, so a re-entrant call restarts cleanly.</summary>
    private Coroutine _snapshotAllPagesCoroutine;

    /// <summary>
    /// Triggers a fresh RT snapshot on every page that is still bound to this notebook — one
    /// page at a time, strictly sequenced. This notebook's pages share a single culling layer
    /// and are physically stacked at the same socket, so a page's checklist camera can only ever
    /// tell its own checklist items apart from a sibling's by that sibling being hidden while
    /// captured (see ExamPage.BeginSnapshot) — which only works if captures never overlap.
    /// Firing every page's capture in the same frame (the old behavior) let overlapping capture
    /// windows bleed one page's content into another's, or into a page camera that hadn't
    /// rendered yet, leaving it blank. Restarts if called again mid-sequence (e.g. picked up
    /// again before the previous pass finished) rather than running two passes concurrently.
    /// </summary>
    private void SnapshotAllPages()
    {
        if (pages == null) return;

        if (_snapshotAllPagesCoroutine != null)
            StopCoroutine(_snapshotAllPagesCoroutine);

        _snapshotAllPagesCoroutine = StartCoroutine(SnapshotAllPagesRoutine());
    }

    private IEnumerator SnapshotAllPagesRoutine()
    {
        if (pages != null)
        {
            foreach (var page in pages)
            {
                if (page == null || page.isRippedOut || !page.isActiveAndEnabled) continue;

                page.SnapshotChecklist();

                // Poll rather than yielding on the page's own Coroutine handle: if the page gets
                // disabled mid-capture (ripped out, or hidden again), Unity silently kills that
                // coroutine without ever resolving a handle someone else is yielding on — which
                // would leave this sequence stuck waiting forever. isActiveAndEnabled naturally
                // breaks the wait the instant that happens instead.
                while (page != null && page.isActiveAndEnabled && page.IsCapturing)
                    yield return null;
            }
        }

        _snapshotAllPagesCoroutine = null;
    }

    /// <summary>
    /// Sent by a non-host client on OnNetworkSpawn for scene-object notebooks.
    /// The server responds with a targeted SetPageReferencesClientRpc so the client
    /// gets its pages[] wired up even though it missed the original broadcast.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestPageReferencesServerRpc(ServerRpcParams rpcParams = default)
    {
        if (pages == null || pages.Length == 0)
        {
            Debug.LogWarning($"[ExamNotebook] RequestPageReferencesServerRpc: pages not yet spawned on server — client {rpcParams.Receive.SenderClientId} will receive empty list.");
            return;
        }

        var pageRefs = new NetworkObjectReference[pages.Length];
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] != null)
                pageRefs[i] = new NetworkObjectReference(pages[i].NetworkObject);
        }

        var targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { rpcParams.Receive.SenderClientId }
            }
        };
        SetPageReferencesClientRpc(pageRefs, targetParams);
    }

    /// <summary>
    /// Called by the server (via PurchaseAndPickUpServerRpc) after the notebook NetworkObject is
    /// spawned. Instantiates fresh copies of the registered page prefabs, spawns each one, and
    /// re-parents it under this notebook — replacing the inline prefab children whose
    /// globalObjectIdHash does not match any registered prefab on clients.
    /// Returns the ordered list of spawned page NetworkObjects so the caller can send
    /// SetPageReferencesClientRpc with authoritative references.
    /// </summary>
    public System.Collections.Generic.List<NetworkObject> SpawnAndWirePages()
    {
        var spawned = new System.Collections.Generic.List<NetworkObject>();

        if (pagePrefab == null)
        {
            Debug.LogWarning($"[ExamNotebook] SpawnAndWirePages: pagePrefab not assigned on {name} — skipping page spawn.");
            return spawned;
        }

        pages = new ExamPage[pageCount];

        for (int i = 0; i < pageCount; i++)
        {
            NetworkObject pageNetObj = Instantiate(pagePrefab);

            // AutoObjectParentSync must be off before Spawn so NGO never replicates
            // a parent change. NetworkTransform is left enabled here so clients receive
            // the initial transform replication during spawn synchronisation. It is
            // disabled on all machines inside ApplyPageReferences (called via
            // SetPageReferencesClientRpc) once every client has resolved the reference
            // and is ready to hand off positioning to LateUpdate.
            pageNetObj.AutoObjectParentSync = false;

            pageNetObj.Spawn(destroyWithScene: true);

            ExamPage page = pageNetObj.GetComponent<ExamPage>();
            if (page == null)
            {
                Debug.LogError($"[ExamNotebook] SpawnAndWirePages: pagePrefab has no ExamPage component.");
                continue;
            }

            // Disable NT on the server after spawn so LateUpdate owns positioning here
            // without NT fighting it. Clients disable theirs inside ApplyPageReferences.
            NetworkTransform nt = pageNetObj.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = false;

            pages[i] = page;
            spawned.Add(pageNetObj);
        }

        return spawned;
    }
    ///
    /// On the host the RPC also runs locally. Because pages[] already holds the real spawned
    /// objects on the server, realPage == pages[i] for every slot and nothing is destroyed —
    /// the loop only disables NT and calls initialization.
    /// </summary>
    [ClientRpc]
    public void SetPageReferencesClientRpc(NetworkObjectReference[] pageRefs, ClientRpcParams clientRpcParams = default)
    {
        StartCoroutine(ApplyPageReferences(pageRefs));
    }

    private IEnumerator ApplyPageReferences(NetworkObjectReference[] pageRefs)
    {
        // Guard: if initialization already ran (e.g. broadcast RPC arrived before a targeted
        // one sent in response to RequestPageReferencesServerRpc), skip the second call so
        // OnValueChanged callbacks are not registered twice.
        if (_pagesInitialized) yield break;
        _pagesInitialized = true;

        // Wait until every referenced NetworkObject is registered locally before touching pages[].
        for (int i = 0; i < pageRefs.Length; i++)
        {
            while (!pageRefs[i].TryGet(out _))
                yield return null;
        }

        pages = new ExamPage[pageRefs.Length];
        for (int i = 0; i < pageRefs.Length; i++)
        {
            if (!pageRefs[i].TryGet(out NetworkObject pageNetObj)) continue;

            ExamPage page = pageNetObj.GetComponent<ExamPage>();
            if (page == null) continue;

            // Disable NT and NGO parent sync — pages follow the notebook via SocketFollow.
            pageNetObj.AutoObjectParentSync = false;
            NetworkTransform nt = pageNetObj.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = false;

            // Point SocketFollow at the notebook root with the anchor's local offset baked in.
            // This avoids reading anchor.position / anchor.rotation through Unity's child-transform
            // hierarchy on inactive GameObjects, which can return stale cached values.
            // SocketFollow runs at order 2, after SyncWorldObjectToBody (order 0/1) has set the
            // notebook's definitive per-frame position and rotation — including arm pitch.
            Transform anchor = (pagePositions != null && i < pagePositions.Length) ? pagePositions[i] : null;
            Vector3    localPos = anchor != null ? anchor.localPosition : Vector3.zero;
            Quaternion localRot = anchor != null ? anchor.localRotation : Quaternion.identity;
            page.SetSocketFollowWithLocalOffset(transform, localPos, localRot);

            page.Initialize(this);
            pages[i] = page;
        }

        ExcludePageCollidersFromCache();
        InitializePageBehavior();

        // If the notebook is delivered inside a supply box, hide the live pages
        // and show the fake stand-ins until it is picked up.
        SetPagesActive(!IsInSupplyBox());
    }

    /// <summary>Wires up NetworkVariable listeners and syncs initial checklist/page state.</summary>
    private void InitializePageBehavior()
    {
        _pageBitmasks = new NetworkVariable<int>[]
        {
            _pageBitmask0, _pageBitmask1, _pageBitmask2, _pageBitmask3, _pageBitmask4
        };

        for (int p = 0; p < pages.Length && p < _pageBitmasks.Length; p++)
        {
            int capturedPage = p;
            pages[p].SetPageIndex(p);
            pages[p].BuildChecklistFromCategory(categoryName);
            pages[p].InitializeChecklistIndices();

            pages[capturedPage].ApplyBitmask(_pageBitmasks[capturedPage].Value, captureSnapshot: false);

            _pageBitmasks[p].OnValueChanged += (_, newValue) =>
            {
                pages[capturedPage].ApplyBitmask(newValue);
                // Fire after ApplyBitmask so IsChecked reflects the new state.
                // Setting AnyBoxChecked here fires on all clients via this NetworkVariable callback
                // so server-side tutorial gates work regardless of who clicked the checkbox.
                ChecklistItem.AnyBoxChecked = true;
                OnAnyCheckboxChecked?.Invoke(this);
            };
        }

        ApplyCurrentPage(_currentPage.Value);
        _currentPage.OnValueChanged += (_, newValue) => ApplyCurrentPage(newValue);

        // The loop above already fired an immediate (possibly overlapping) capture per page via
        // RefreshLockStates/ApplyBitmask. Follow up with one guaranteed-correct, strictly
        // sequenced pass so every page's initial render is right regardless of what happened
        // during that noisy synchronous setup — see SnapshotAllPages. Skipped for supply-box
        // delivery: SetPagesActive(false) runs immediately after this method returns, so pages
        // aren't visible yet anyway — OnPickedUpAllClients/OnPickedUp trigger the real pass once
        // they're actually activated.
        if (!IsInSupplyBox())
            SnapshotAllPages();
    }

    /// <summary>
    /// Enables the checklist on the active page and disables it on all others.
    /// Safe to call on any client at any time.
    /// </summary>
    private void ApplyCurrentPage(int page)
    {
        currentPage = page;
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] == null) continue;
            pages[i].SetChecklistInteractable(i == currentPage && !pages[i].isRippedOut);
        }
    }

    /// <summary>
    /// Shows or hides this notebook on all clients by toggling its renderers, collider, and highlight.
    /// Use this to reveal the notebook at a specific tutorial beat. The GameObject stays active
    /// so NGO scene-object registration is not disrupted.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (IsServer)
        {
            SetVisibleClientRpc(visible);
        }
        else
        {
            // Guard: RPCs require the NetworkObject to be spawned. If called before
            // NGO has registered this scene object on the client (e.g. during
            // StartGameClientRpc → StartCampaign), skip silently — the server will
            // broadcast the authoritative state via ClientRpc regardless.
            if (!IsSpawned) return;
            SetVisibleServerRpc(visible);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetVisibleServerRpc(bool visible) => SetVisibleClientRpc(visible);

    [ClientRpc]
    private void SetVisibleClientRpc(bool visible)
    {
        ApplyVisibility(visible);
    }

    private void ApplyVisibility(bool visible)
    {
        foreach (MeshRenderer mr in GetComponentsInChildren<MeshRenderer>(true))
            mr.enabled = visible;

        if (TryGetComponent<BoxCollider>(out var col))
            col.enabled = visible;

        if (TryGetComponent<HighlightEffect>(out var highlight))
            highlight.enabled = visible;
    }

    /// <summary>
    /// Detects if the notebook is currently parented inside a SupplyBox.
    /// </summary>
    private bool IsInSupplyBox() => GetComponentInParent<SupplyBox>() != null;

    /// <summary>
    /// Toggles visibility between the actual spawned ExamPage objects and the
    /// inactive prefab stand-ins (pagePositions).
    /// </summary>
    private void SetPagesActive(bool active)
    {
        if (pages == null) return;

        for (int i = 0; i < pages.Length; i++)
        {
            var page = pages[i];
            if (page == null) continue;

            // Actual page is only active if the notebook wants them active AND it hasn't been ripped out.
            // If it IS ripped out, its own logic handles its activity.
            if (!page.isRippedOut)
                page.gameObject.SetActive(active);

            // Stand-in is only active if the notebook wants stand-ins AND this specific page hasn't been ripped out.
            if (pagePositions != null && i < pagePositions.Length && pagePositions[i] != null)
            {
                pagePositions[i].gameObject.SetActive(!active && !page.isRippedOut);
            }
        }
    }


    /// <summary>
    /// Called by ExamPage when the local player clicks a checkbox.
    /// Routes through the notebook's ServerRpc since nested NetworkObjects can't send RPCs.
    /// </summary>
    public void SetCheckboxChecked(int pageIndex, int itemIndex, bool value)
    {
        SetCheckboxServerRpc(pageIndex, itemIndex, value);
        // OnAnyCheckboxChecked is fired by the NetworkVariable.OnValueChanged callback
        // (after ApplyBitmask), so IsChecked is authoritative when AllVisibleBoxesChecked is read.
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetCheckboxServerRpc(int pageIndex, int itemIndex, bool value)
    {
        if (pageIndex < 0 || pageIndex >= _pageBitmasks.Length) return;
        if (itemIndex < 0 || itemIndex >= 32) return;

        int bitmask = _pageBitmasks[pageIndex].Value;
        if (value)
            bitmask |= (1 << itemIndex);
        else
            bitmask &= ~(1 << itemIndex);

        _pageBitmasks[pageIndex].Value = bitmask;
    }

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        IsChecking = true;
        playerPickupController.PlayerAnimationController.RightArmIKTarget = ikAnimationTarget;
        StartCoroutine(TurnRightArmRigOff());
    }
    public override void OnPickedUp()
    {
        base.OnPickedUp();
        // Always restore live pages when picked up out of a box or off the floor.
        SetPagesActive(true);
        // Re-render RTs immediately on the picking-up client. Other clients are handled by
        // OnPickedUpNetworked → OnPickedUpAllClients once the NetworkVariable propagates.
        SnapshotAllPages();
        // Notify tutorial systems on all clients that a checklist has been acquired.
        AnyExamNotebookPickedUp = true;
        OnAnyExamNotebookPickedUp?.Invoke();
    }

    public override void OnDropped()
    {
        base.OnDropped();
        // If dropped back into a box (rare but possible), hide pages again.
        // Otherwise keep them active for floor visibility.
        SetPagesActive(!IsInSupplyBox());
    }


    IEnumerator TurnRightArmRigOff()
    {
        playerPickupController.PlayerAnimationController.TurnRightArmRigOnAndOff(.25f, .5f);
        yield return new WaitForSeconds(.5f);
        IsChecking = false;
    }

    public override void OnEquipped(PlayerPickupController player)
    {
        base.OnEquipped(player);

        SetRedPencilActive(player, true);
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);

        // base.OnUnequip calls SetInteractable(true), which walks GetComponentsInChildren<InteractableCollider>
        // and re-enables colliders on pages that are still bound to the notebook. Re-disable them.
        foreach (var page in pages)
        {
            if (page == null || page.isRippedOut) continue;
            page.SetInteractable(false);
        }

        SetRedPencilActive(player, false);
    }

    /// <summary>
    /// Activates or deactivates the Red Pencil in every arm container on the player so
    /// both the owner (camera arm) and observers (body arm) see the correct state.
    /// </summary>
    private void SetRedPencilActive(PlayerPickupController player, bool active)
    {
        const string RedPencilName = "RedPencil";

        ObjectContainer[] containers =
        {
            player.RightArmCamObjectContainer,
            player.RightArmBodyObjectContainer,
            player.LeftArmCamObjectContainer,
            player.LeftArmBodyObjectContainer,
        };

        foreach (var container in containers)
        {
            if (container == null) continue;
            foreach (var itemHeld in container.ItemsHeld)
            {
                if (itemHeld != null && itemHeld.ItemData != null && itemHeld.ItemData.name == RedPencilName)
                    itemHeld.gameObject.SetActive(active);
            }
        }
    }

    public override void OnStartUse()
    {
        if (addingToFolder)
        {
            return;
        }

        // Re-render all page RTs so the overlay is always up-to-date when the view opens.
        SnapshotAllPages();

        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        playerPickupController.CanPickUpAndPlace = false;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(false);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(false);

        _isInDrawMode = true;
        _controllerSelectedItem = null;

        // Default to controller navigation (cursor hidden) if a gamepad is connected;
        // otherwise show the free mouse cursor for point-and-click checkbox interaction.
        if (Gamepad.current != null)
        {
            UIController.Instance.HideCursor();
            ChecklistItem[] items = GetNavigableChecklistItems();
            if (items.Length > 0)
                SetChecklistControllerSelection(items[0]);
        }
        else
        {
            UIController.Instance.ShowCursor();
        }

        UIController.Instance.ShowBackButton(ExitDrawMode);
    }

    void ExitDrawMode()
    {
        playerPickupController.CanPickUpAndPlace = true;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(true);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(true);
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);

        _isInDrawMode = false;
        ClearChecklistControllerSelection();

        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();
    }

    private void Update()
    {
        if (!_isInDrawMode) return;
        UpdateChecklistControllerNavigation();
    }

    /// <summary>
    /// Returns the checklist items on the currently active page that can be navigated to
    /// with a controller — unlocked and currently visible — ordered top-to-bottom to match
    /// their on-page layout.
    /// </summary>
    private ChecklistItem[] GetNavigableChecklistItems()
    {
        if (pages == null || currentPage < 0 || currentPage >= pages.Length) return System.Array.Empty<ChecklistItem>();

        ExamPage page = pages[currentPage];
        if (page == null || page.ChecklistItems == null) return System.Array.Empty<ChecklistItem>();

        return page.ChecklistItems
            .Where(item => item != null && !item.IsLocked && item.gameObject.activeInHierarchy)
            .OrderByDescending(item => item.transform.position.y)
            .ToArray();
    }

    /// <summary>
    /// Drives controller (gamepad) checklist navigation while draw mode is open:
    /// left stick / d-pad vertical input moves the highlighted checkbox, the south button
    /// toggles it, and any mouse/keyboard activity clears the highlight and restores the
    /// free mouse cursor.
    /// </summary>
    private void UpdateChecklistControllerNavigation()
    {
        // ── Mouse/keyboard activity always wins: clear controller highlight, show cursor ──
        bool mkActivity = (Mouse.current != null &&
                            (Mouse.current.delta.ReadValue().sqrMagnitude > 0.01f ||
                             Mouse.current.leftButton.wasPressedThisFrame ||
                             Mouse.current.rightButton.wasPressedThisFrame))
                           || (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (mkActivity)
        {
            if (_controllerSelectedItem != null)
                ClearChecklistControllerSelection();
            UIController.Instance.ShowCursor();
            return;
        }

        Gamepad gp = Gamepad.current;
        if (gp == null) return;

        ChecklistItem[] items = GetNavigableChecklistItems();
        if (items.Length == 0) return;

        Vector2 leftStick = gp.leftStick.ReadValue();
        bool navUp = gp.dpad.up.wasPressedThisFrame;
        bool navDown = gp.dpad.down.wasPressedThisFrame;

        if (leftStick.y > 0.5f && Time.time >= _stickNavTime) { navUp = true; _stickNavTime = Time.time + ChecklistStickNavCooldown; }
        if (leftStick.y < -0.5f && Time.time >= _stickNavTime) { navDown = true; _stickNavTime = Time.time + ChecklistStickNavCooldown; }

        bool confirmPressed = gp.buttonSouth.wasPressedThisFrame;

        if (!navUp && !navDown && !confirmPressed) return;

        // Any controller input takes over navigation — hide the free mouse cursor by default.
        UIController.Instance.HideCursor();

        int currentIndex = _controllerSelectedItem != null ? System.Array.IndexOf(items, _controllerSelectedItem) : -1;

        if (currentIndex < 0)
        {
            SetChecklistControllerSelection(items[0]);
        }
        else if (navUp || navDown)
        {
            int dir = navDown ? 1 : -1;
            int newIndex = Mathf.Clamp(currentIndex + dir, 0, items.Length - 1);
            SetChecklistControllerSelection(items[newIndex]);
        }
        else if (confirmPressed)
        {
            items[currentIndex].ActivateViaController();
        }
    }

    /// <summary>Moves the controller highlight to <paramref name="item"/>, clearing the previous one.</summary>
    private void SetChecklistControllerSelection(ChecklistItem item)
    {
        if (_controllerSelectedItem == item) return;

        _controllerSelectedItem?.SetControllerSelected(false);
        _controllerSelectedItem = item;
        _controllerSelectedItem?.SetControllerSelected(true);
    }

    /// <summary>Clears the controller highlight, if any.</summary>
    private void ClearChecklistControllerSelection()
    {
        _controllerSelectedItem?.SetControllerSelected(false);
        _controllerSelectedItem = null;
    }

    public void AddToFolder(FolderController folder)
    {
        Debug.Log($"[ExamNotebook] AddToFolder called on client {NetworkManager.Singleton.LocalClientId} | playerPickupController={(playerPickupController != null ? playerPickupController.name : "NULL")} | currentPage={currentPage} | addingToFolder={addingToFolder}");
        addingToFolder = true;
        pages[currentPage].SetChecklistInteractable(false);

        playerPickupController.PlayerAnimationController.SetAnimTrigger("RipOutPage");
        SFXController.Instance.PlayAtPosition(ripOutSound, transform.position);
        StartCoroutine(WaitAndParent(pages[currentPage], folder));
        Debug.Log("Rip out and add to folder");
    }

    IEnumerator WaitAndParent(ExamPage rippedPage, FolderController folder)
    {
        Debug.Log($"[ExamNotebook] WaitAndParent started on client {NetworkManager.Singleton.LocalClientId}");
        yield return new WaitForSeconds(.5f);

        int pageIndex = System.Array.IndexOf(pages, rippedPage);
        Debug.Log($"[ExamNotebook] WaitAndParent — pageIndex={pageIndex} | rippedPage={(rippedPage != null ? rippedPage.name : "NULL")} | folder={(folder != null ? folder.name : "NULL")}");

        // All three calls below must originate from the server so their ClientRpc broadcasts
        // reach every connected client. A non-host client calling a [ClientRpc] directly only
        // executes it locally — the other clients never see it. Routing through [ServerRpc]
        // wrappers ensures the server is always the one invoking the ClientRpc, regardless of
        // which client initiated the rip-out.
        BroadcastRipOutPageServerRpc(pageIndex);

        yield return new WaitForSeconds(.4f);

        // Route AddDocument through a ServerRpc so the server resolves the real spawned
        // NetworkObject instances. On a pure client, pages[pageIndex] is the local prefab
        // child instantiated by NGO — NOT the separately-spawned NetworkObject. Calling
        // PlaceInSlotServerRpc on that local instance causes a NullReferenceException inside
        // __endSendServerRpc because NGO has no network state for that unspawned object.
        Debug.Log($"[ExamNotebook] WaitAndParent — sending RequestAddToFolderServerRpc pageIndex={pageIndex} folder={folder?.name ?? "NULL"} on client {NetworkManager.Singleton.LocalClientId}");
        RequestAddToFolderServerRpc(pageIndex, new NetworkObjectReference(folder.NetworkObject));

        AdvancePageServerRpc(pageIndex);

        // SetupMaterial is deferred inside ResetPageClientRpc to ensure PlaceInSlotClientRpc
        // has detached the page from this hierarchy before HighlightEffect scans children.
        BroadcastResetPageServerRpc(pageIndex);
        addingToFolder = false;
    }

    /// <summary>
    /// Asks the server to place the ripped page (identified by index) into the folder.
    /// The server resolves the real spawned NetworkObject for the page — avoiding the
    /// NullReferenceException that occurs when a client RPC is sent from an unspawned
    /// prefab-child instance rather than a true NetworkObject.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestAddToFolderServerRpc(int pageIndex, NetworkObjectReference folderRef)
    {
        if (pageIndex < 0 || pageIndex >= pages.Length)
        {
            Debug.LogError($"[ExamNotebook] RequestAddToFolderServerRpc: invalid pageIndex={pageIndex}");
            return;
        }

        if (!folderRef.TryGet(out NetworkObject folderNetObj))
        {
            Debug.LogError($"[ExamNotebook] RequestAddToFolderServerRpc: could not resolve folder NetworkObject");
            return;
        }

        FolderController folder = folderNetObj.GetComponent<FolderController>();
        if (folder == null)
        {
            Debug.LogError($"[ExamNotebook] RequestAddToFolderServerRpc: folder NetworkObject has no FolderController");
            return;
        }

        // On the server, pages[pageIndex] is the real spawned NetworkObject — safe to call RPCs on.
        // playerPickupController is not needed here because dropObject=false bypasses DropObject.
        ExamPage serverPage = pages[pageIndex];
        Debug.Log($"[ExamNotebook] RequestAddToFolderServerRpc: pageIndex={pageIndex} page={serverPage?.name ?? "NULL"} IsSpawned={serverPage?.NetworkObject?.IsSpawned} NetworkObjectId={serverPage?.NetworkObject?.NetworkObjectId}");
        folder.AddDocument(serverPage, playerPickupController, false);

        // Mirrors FolderController.SyncDocumentAddedServerRpc's doc.SetInteractableNetworked(...)
        // for idCard/application, so a late-joining client still resolves the correct state.
        // Also refresh the server's own live collider/interactable state directly and
        // immediately via FolderItem.RefreshFolderState — the single source of truth.
        serverPage.SetInteractableNetworked(folder.IsOpen && !folder.IsHeld);
        serverPage.RefreshFolderState();

        // PlacePageInSlotNetworked (called inside AddDocument) no longer sends PlaceInSlotClientRpc
        // via the page's NetworkObject because that RPC is silently dropped on non-host clients
        // when triggered through a server-side ServerRpc call chain. Broadcast the slot assignment
        // here instead, via the notebook's NetworkObject which is known to reach all clients.
        Transform slot = folder.GetSlotForPage(serverPage.ItemData.name);
        if (slot == null)
        {
            Debug.LogError($"[ExamNotebook] RequestAddToFolderServerRpc: no slot found for {serverPage.ItemData.name}");
            return;
        }

        NetworkObject slotOwner = slot.GetComponentInParent<NetworkObject>();
        if (slotOwner == null)
        {
            Debug.LogError($"[ExamNotebook] RequestAddToFolderServerRpc: slot '{slot.name}' has no NetworkObject parent");
            return;
        }

        string slotPath = FolderController.GetRelativePath(slotOwner.transform, slot);
        NotifyPagePlacedInFolderClientRpc(
            new NetworkObjectReference(serverPage.NetworkObject),
            new NetworkObjectReference(slotOwner),
            slotPath,
            slot.position,
            slot.rotation);
    }

    /// <summary>
    /// Received on all clients. Detaches the page from the notebook hierarchy and registers it
    /// with FolderController for LateUpdate-based slot following. Sent via the notebook's
    /// NetworkObject so it reliably reaches all clients — unlike the page's own PlaceInSlotClientRpc
    /// which is silently dropped when triggered through a server-side ServerRpc call chain.
    /// </summary>
    [ClientRpc]
    private void NotifyPagePlacedInFolderClientRpc(
        NetworkObjectReference pageRef,
        NetworkObjectReference slotOwnerRef,
        string slotPath,
        Vector3 position,
        Quaternion rotation)
    {
        if (!pageRef.TryGet(out NetworkObject pageNetObj))
        {
            Debug.LogError($"[ExamNotebook] NotifyPagePlacedInFolderClientRpc: could not resolve page on client {NetworkManager.Singleton.LocalClientId}");
            return;
        }

        if (!slotOwnerRef.TryGet(out NetworkObject slotOwner))
        {
            Debug.LogError($"[ExamNotebook] NotifyPagePlacedInFolderClientRpc: could not resolve slot owner on client {NetworkManager.Singleton.LocalClientId}");
            return;
        }

        PickableObject page = pageNetObj.GetComponent<PickableObject>();
        FolderController folder = slotOwner.GetComponent<FolderController>();
        if (page == null || folder == null) return;

        Transform slot = string.IsNullOrEmpty(slotPath)
            ? slotOwner.transform
            : slotOwner.transform.Find(slotPath);

        if (slot == null)
        {
            Debug.LogWarning($"[ExamNotebook] NotifyPagePlacedInFolderClientRpc: slot '{slotPath}' not found on {slotOwner.name}");
            return;
        }

        // Detach the page from the notebook hierarchy without NGO replicating the change.
        pageNetObj.AutoObjectParentSync = false;
        if (page.transform.parent != null)
            page.transform.SetParent(null, worldPositionStays: true);

        page.transform.position = position;
        page.transform.rotation = rotation;

        NetworkTransform nt = page.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        Rigidbody rb = page.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        Debug.Log($"[ExamNotebook] NotifyPagePlacedInFolderClientRpc: registering {page.name} → {folder.name}/{slot.name} on client {NetworkManager.Singleton.LocalClientId}");
        page.SetSocketFollow(slot);

        // FolderController.AddDocument (server-only) already set insideThisFolder via AddToFolder,
        // but that call never reaches any client — this RPC is the only broadcast every client
        // (including the server's own instance is already set, this is idempotent for it too)
        // receives for exam pages. Without this, insideThisFolder stays null on every client,
        // which mirrors what SyncDocumentAddedClientRpc already does for idCard/application:
        // both insideThisFolder and folder.documents must be consistent on every machine, or
        // SetInteractable's folder-guard and RemovePromFolder's later pickup path evaluate
        // differently per machine — the root cause of pages being grabbable on some clients
        // but stuck non-interactable (colliders never re-enabled) on others.
        if (page is FolderItem folderItem)
        {
            folderItem.insideThisFolder = folder;
            if (!folder.documents.Contains(page))
                folder.documents.Add(page);
        }

        // insideThisFolder/documents are now consistent on this client (set just above), so
        // refresh this page's collider/interactable state directly from the folder's live
        // open/held state — see FolderItem.RefreshFolderState, the single source of truth.
        // SetSocketFollow above unconditionally disables physics colliders via
        // PickableColliderController.SetHeld(), which runs before this and would otherwise be
        // left as the last word on the page's collider state.
        if (page is FolderItem folderItemToRefresh)
        {
            folderItemToRefresh.RefreshFolderState();
        }

        AnyPageFiled = true;
        OnAnyNotebookPageFiled?.Invoke();
    }

    /// <summary>
    /// Routes RipOutPageClientRpc through the server so it broadcasts to all clients
    /// regardless of whether the initiating machine is the host or a pure client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void BroadcastRipOutPageServerRpc(int pageIndex)
    {
        RipOutPageClientRpc(pageIndex);
    }

    /// <summary>
    /// Routes ResetPageClientRpc through the server so it broadcasts to all clients
    /// regardless of whether the initiating machine is the host or a pure client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void BroadcastResetPageServerRpc(int pageIndex)
    {
        ResetPageClientRpc(pageIndex);
    }

    /// <summary>
    /// Asks the server to advance _currentPage to pageIndex+1.
    /// Using a ServerRpc ensures this works whether called from host or a pure client.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void AdvancePageServerRpc(int pageIndex)
    {
        int nextPage = pageIndex + 1;
        if (nextPage < pages.Length)
            _currentPage.Value = nextPage;
    }

    /// <summary>
    /// Broadcasts the RipOut animator trigger and marks the page as detached on all clients.
    /// Called before the network placement RPC so the animation plays in sync everywhere.
    /// </summary>
    [ClientRpc]
    private void RipOutPageClientRpc(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= pages.Length) return;
        ExamPage page = pages[pageIndex];
        page.isRippedOut = true;
        page.CanPickUpManually = true;
        page.pageAnimator.SetTrigger("RipOut");
    }

    /// <summary>
    /// Broadcasts the Reset animator trigger after the page has been placed into the folder slot.
    /// Also refreshes the notebook's InteractableCollider cache and rebuilds HighlightEffect's
    /// material list on every client once the page has fully detached from this hierarchy.
    /// </summary>
    [ClientRpc]
    private void ResetPageClientRpc(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= pages.Length) return;
        pages[pageIndex].pageAnimator.SetTrigger("Reset");

        // Rebuild the cache excluding all page colliders (ripped or not) so SetInteractable
        // on the notebook never re-enables a page's own physics collider.
        ExcludePageCollidersFromCache();

        // Poll each frame until the page has detached, then rebuild HighlightEffect.
        // A fixed one-frame delay is not reliable because PlaceInSlotClientRpc arrives from
        // a different NetworkObject with no ordering guarantee relative to this RPC.
        StartCoroutine(RefreshHighlightAfterDetach(pages[pageIndex]));
    }

    /// <summary>
    /// Snaps every un-ripped page to its anchor socket on the notebook each frame.
    /// Pages are separate NetworkObjects and are not NGO-parented to the notebook;
    /// SocketFollow drives their world transform instead. This LateUpdate only handles
    /// scale, which SocketFollow does not manage.
    /// </summary>
    private void LateUpdate()
    {
        if (pageScaleReference == null) return;

        Vector3 targetScale = pageScaleReference.lossyScale;
        for (int i = 0; i < pages.Length; i++)
        {
            if (pages[i] == null || pages[i].isRippedOut) continue;
            pages[i].transform.localScale = targetScale;
        }
    }

    /// <summary>
    /// Waits until the given page is ripped out (its isRippedOut flag is set), then
    /// rebuilds HighlightEffect's material list. Pages are no longer parented to the
    /// notebook so we can't rely on IsChildOf — the ripped flag is the reliable signal.
    /// </summary>
    private IEnumerator RefreshHighlightAfterDetach(ExamPage page)
    {
        while (page != null && !page.isRippedOut)
            yield return null;

        // Give the folder placement RPC one extra frame to move the renderer.
        yield return null;

        GetComponent<HighlightEffect>().Refresh();
    }
}
