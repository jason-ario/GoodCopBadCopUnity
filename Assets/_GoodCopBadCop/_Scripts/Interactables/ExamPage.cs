using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

public class ExamPage : FolderItem
{
    // Built dynamically at runtime by BuildChecklistFromCategory — no longer wired in the
    // Inspector. Kept as a plain (non-serialized) field so all existing index-based logic
    // (sorting, bitmask, lock states) works unchanged against whatever was just spawned.
    private ChecklistItem[] _checklistItems;
    private ExamNotebook notebook;
    private int pageIndex;
    public Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool isRippedOut;
    [SerializeField] private GameObject container;

    [Header("Dynamic Checklist Item Spawning")]
    /// <summary>The base "Checklist Item" prefab instantiated per anomaly at runtime.</summary>
    [SerializeField] private ChecklistItem checklistItemPrefab;

    /// <summary>Parent transform new checklist items are instantiated under (the page's "Exam Notebook Contents" child).</summary>
    [SerializeField] private Transform checklistItemsParent;

    /// <summary>Local position of the first (bottom-most) checklist item slot.</summary>
    [SerializeField] private Vector3 checklistItemStartLocalPosition = new Vector3(-0.016f, -0.478f, 0.019f);

    /// <summary>Vertical distance in local space between consecutive checklist item slots.</summary>
    [SerializeField] private float checklistItemSpacingY = 0.1397143f;

    /// <summary>
    /// Total number of printed checklist lines on the page artwork (the largest anomaly
    /// category size across the progression asset — currently Documentation/Vitals/Behavior/
    /// Supernatural all have 7). Used to compute a fixed top-of-page anchor so the checklist
    /// always starts right below the header and grows downward, regardless of how many
    /// anomalies the current category actually has (e.g. Physical's 5). Without this, pages
    /// with fewer anomalies than the max would anchor to the bottom-most printed line instead
    /// and leave a gap under the header.
    /// </summary>
    [SerializeField] private int checklistTotalSlotCount = 7;

    /// <summary>Local rotation (Euler, degrees) applied to every spawned checklist item.</summary>
    [SerializeField] private Vector3 checklistItemLocalRotationEuler = new Vector3(0f, 0f, 90f);

    /// <summary>Local scale applied to every spawned checklist item.</summary>
    [SerializeField] private Vector3 checklistItemLocalScale = new Vector3(0.08675341f, 0.930651f, 0.09206503f);

    // ── RenderTexture overlay system ─────────────────────────────────────────
    /// <summary>
    /// Visual-side checklist items, resolved from _checklistItems at Awake.
    /// Each ChecklistVisual lives on the same GameObject as its ChecklistItem.
    /// </summary>
    private ChecklistVisual[] _visualItems;

    /// <summary>The orthographic camera inside Exam Notebook Contents that renders to the RT.</summary>
    [SerializeField] private Camera _checklistCamera;

    /// <summary>The MeshRenderer on Plane.002 whose material exposes _OverlayMap.</summary>
    [SerializeField] private SkinnedMeshRenderer _paperRenderer;

    /// <summary>
    /// Project-asset RenderTexture used as a descriptor template. A runtime clone is created
    /// per page instance so each page gets a unique RT with the exact same GPU flags as the
    /// asset (depth format, color format, MSAA, etc.) — avoiding URP DBuffer assertion failures
    /// that occur when descriptor properties are incomplete on hand-constructed RenderTextures.
    /// </summary>
    [SerializeField] private RenderTexture _renderTextureTemplate;

    /// <summary>
    /// How long the checklist camera stays active after a checkbox state change.
    /// Must cover Checkbox.WaitAndShowCheckmark's 0.15 s delay before the checkmark GameObject
    /// is even activated, PLUS the full "Cross Drawn In" Animator clip length (~0.5167 s) that
    /// starts playing once it is — i.e. at least ~0.667 s end-to-end from the click — plus a
    /// small margin so the last frames of the draw are never cut off (see Checkbox.cs and
    /// X.controller's "Cross Drawn In" state).
    /// </summary>
    [SerializeField] private float _drawAnimationDuration = 0.85f;

    private static readonly int OverlayMapProperty = Shader.PropertyToID("_OverlayMap");

    private RenderTexture _renderTexture;
    private Material _paperMaterialInstance;
    private Coroutine _snapshotCoroutine;

    /// <summary>
    /// All currently-enabled ExamPage instances, used by <see cref="SnapshotRoutine"/> to
    /// temporarily hide sibling pages' checklist artwork so this page's checklist camera —
    /// which culls by the shared HiddenUI layer only, not by which page owns the content —
    /// never picks up a neighboring page's checklist items when pages are physically close
    /// together (stacked in a notebook, or two players standing near each other). Only
    /// renderers are toggled here, never colliders or transforms, so click raycasting on
    /// checkboxes is completely unaffected.
    /// </summary>
    private static readonly List<ExamPage> _activePages = new List<ExamPage>();

    /// <summary>True while this page's checklist camera is actively capturing a snapshot.</summary>
    private bool _isSnapshotting;

    /// <summary>Peers this page's current/most-recent snapshot has requested be hidden.</summary>
    private readonly List<ExamPage> _peersHiddenByThisSnapshot = new List<ExamPage>();

    /// <summary>
    /// How many other pages currently want THIS page's checklist renderers hidden. Reference
    /// counted (rather than a plain bool) so overlapping hide requests from multiple
    /// simultaneously-snapshotting peers can never restore visibility early.
    /// </summary>
    private int _hideRequestCount;
    private Renderer[] _checklistRenderersCache;
    private bool[] _preHideRendererEnabledStates;

    /// <summary>
    /// Original local positions of each checklist item captured at Awake.
    /// Used as the position pool when re-sorting by lock state so repeated
    /// calls to RefreshLockStates always produce a consistent result.
    /// </summary>
    private Vector3[] _originalItemPositions;

    /// <summary>
    /// Tracks the last interactable state applied by SetChecklistInteractable so that
    /// RefreshLockStates can re-apply the correct state rather than relying on the
    /// transient IsChecking animation flag.
    /// </summary>
    private bool _checklistInteractable;
    // ─────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        SetupRenderTexture();
    }

    /// <summary>
    /// Resolves each ChecklistVisual from the same GameObject as its ChecklistItem,
    /// eliminating the need for a separate serialized array.
    /// </summary>
    private void CacheVisualItems()
    {
        if (_checklistItems == null || _checklistItems.Length == 0)
        {
            _visualItems = System.Array.Empty<ChecklistVisual>();
            return;
        }

        _visualItems = new ChecklistVisual[_checklistItems.Length];
        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (_checklistItems[i] != null)
                _visualItems[i] = _checklistItems[i].GetComponent<ChecklistVisual>();
        }
    }

    /// <summary>
    /// Snapshots the initial local positions of all checklist items so that
    /// RefreshLockStates can always sort from a consistent baseline rather than
    /// operating on already-sorted positions from a previous call.
    /// </summary>
    private void CacheOriginalItemPositions()
    {
        if (_checklistItems == null)
        {
            _originalItemPositions = System.Array.Empty<Vector3>();
            return;
        }

        _originalItemPositions = new Vector3[_checklistItems.Length];
        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (_checklistItems[i] != null)
                _originalItemPositions[i] = _checklistItems[i].transform.localPosition;
        }
    }

    /// <summary>
    /// Creates a unique RenderTexture for this page instance, assigns it to the checklist
    /// camera, and stamps it onto a new material instance so pages never share RT output.
    /// </summary>
    private void SetupRenderTexture()
    {
        if (_checklistCamera == null || _paperRenderer == null)
        {
            Debug.LogWarning($"[ExamPage] SetupRenderTexture: '{name}' is missing _checklistCamera or _paperRenderer — this page will keep rendering the shared default checklist texture instead of a unique per-instance one, which can make its checklist appear to show another page's content.");
            return;
        }

        if (_paperRenderer.sharedMaterials.Length < 2)
        {
            Debug.LogWarning($"[ExamPage] SetupRenderTexture: '{name}' has fewer than 2 material slots on its paper renderer — cannot create a unique overlay material instance, so this page will keep rendering the shared default checklist texture instead of a unique per-instance one.");
            return;
        }

        // Clone the descriptor from the project-asset template so the runtime RT inherits all
        // GPU flags (depth format, color format, MSAA, etc.) that URP's DBuffer/Decal pass
        // requires. Constructing a RenderTexture with a partial descriptor (e.g. depth=0,
        // ARGB32) omits flags that the asset importer sets, causing an assertion in
        // DBufferRenderPass.Setup when the camera becomes active.
        RenderTextureDescriptor desc = _renderTextureTemplate != null
            ? _renderTextureTemplate.descriptor
            : new RenderTextureDescriptor(1024, 1024, RenderTextureFormat.Default, 24);

        _renderTexture = new RenderTexture(desc)
        {
            name = $"ChecklistRT_{GetEntityId()}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _renderTexture.Create();

        _checklistCamera.targetTexture = _renderTexture;

        // Create a per-instance material so pages don't share the same texture slot.
        // _OverlayMap_ST (tiling/offset) is intentionally inherited from the shared material.
        _paperMaterialInstance = new Material(_paperRenderer.sharedMaterials[1]);
        _paperMaterialInstance.SetTexture(OverlayMapProperty, _renderTexture);

        Material[] newSlots = new Material[_paperRenderer.sharedMaterials.Length];
        for (int i = 0; i < newSlots.Length; i++)
            newSlots[i] = _paperMaterialInstance;
        _paperRenderer.materials = newSlots;

        // Camera starts inactive — only enabled briefly when snapshotting.
        _checklistCamera.gameObject.SetActive(false);
    }

    /// <summary>
    /// Enables the checklist camera for one frame so it writes the current visual state
    /// into the RenderTexture, then deactivates it. Restarts if called while already running.
    /// Called by ExamNotebook after SetPagesActive(true) so the RT is populated on every
    /// client that just activated the page (e.g. picked up from a supply box).
    /// </summary>
    /// <summary>
    /// Auto-populates this page's checklist by instantiating a fresh "Checklist Item" for every
    /// anomaly in <paramref name="categoryName"/>'s progression list, instead of relying on
    /// hand-authored, pre-placed slots baked into the prefab (which drifted out of sync and could
    /// leave stray/duplicate items behind). Called by ExamNotebook right before
    /// <see cref="InitializeChecklistIndices"/> whenever a notebook spawns its pages, so every
    /// page always reflects the current category's anomaly list with exactly the items it needs —
    /// no more, no less.
    ///
    /// Any previously spawned items (e.g. from a prior call) are destroyed first so repeated
    /// calls never produce duplicates. Items are laid out top-to-bottom in the progression
    /// asset's authored order using the page's configured start position/spacing. Locked/unlocked
    /// visibility and sorting is applied afterwards by InitializeChecklistIndices → RefreshLockStates,
    /// which already reads unlocked state from AnomalyUnlockManager.
    /// </summary>
    public void BuildChecklistFromCategory(string categoryName)
    {
        ClearChecklistItems();

        if (checklistItemPrefab == null)
        {
            Debug.LogWarning($"[ExamPage] BuildChecklistFromCategory: '{name}' has no checklistItemPrefab assigned — no checklist items were spawned.");
            return;
        }

        if (string.IsNullOrEmpty(categoryName))
        {
            Debug.LogWarning($"[ExamPage] BuildChecklistFromCategory: '{name}' has no categoryName assigned on its ExamNotebook — no checklist items were spawned.");
            return;
        }

        if (AnomalyUnlockManager.Instance == null)
        {
            Debug.LogWarning($"[ExamPage] BuildChecklistFromCategory: no AnomalyUnlockManager.Instance in the scene — no checklist items were spawned on '{name}'.");
            return;
        }

        string[] anomalyNames = AnomalyUnlockManager.Instance.GetAnomalyTypeNamesForCategory(categoryName);
        Transform parent = checklistItemsParent != null ? checklistItemsParent : transform;

        _checklistItems = new ChecklistItem[anomalyNames.Length];
        for (int i = 0; i < anomalyNames.Length; i++)
        {
            ChecklistItem item = Instantiate(checklistItemPrefab, parent);
            item.name = $"Checklist Item {i}";
            item.gameObject.layer = parent.gameObject.layer;

            // checklistItemStartLocalPosition is the bottom-most printed line, shared by every
            // page regardless of category. To make the checklist always start right below the
            // header (instead of bottom-anchoring and leaving a gap under the header for
            // categories with fewer than checklistTotalSlotCount anomalies), compute the fixed
            // top-most slot and fill downward from there: the first anomaly (i == 0) goes in
            // the top-most slot, and each subsequent anomaly moves one slot down toward the
            // bottom of the page.
            int slotFromTop = checklistTotalSlotCount - 1 - i;
            Transform t = item.transform;
            t.localPosition = checklistItemStartLocalPosition + new Vector3(0f, slotFromTop * checklistItemSpacingY, 0f);
            t.localRotation = Quaternion.Euler(checklistItemLocalRotationEuler);
            t.localScale = checklistItemLocalScale;

            item.SetExamPage(this);
            item.SetAnomalyTypeName(anomalyNames[i]);

            _checklistItems[i] = item;
        }

        CacheVisualItems();
        CacheOriginalItemPositions();
    }

    /// <summary>Destroys any checklist items previously spawned by BuildChecklistFromCategory.</summary>
    private void ClearChecklistItems()
    {
        if (_checklistItems == null) return;

        foreach (ChecklistItem item in _checklistItems)
        {
            if (item != null)
                Destroy(item.gameObject);
        }

        _checklistItems = null;
    }

    /// <summary>
    /// Fires this page's capture immediately, restarting it if one is already in flight.
    /// Used for checkbox clicks, where only the one currently-interactable page can ever be
    /// clicked, so there is no sibling to overlap with. For whole-notebook batches (init,
    /// pickup) ExamNotebook instead calls this once per page and polls <see cref="IsCapturing"/>
    /// before moving to the next, so every page's capture is strictly sequenced and none can
    /// overlap with a sibling's.
    /// </summary>
    public void SnapshotChecklist()
    {
        if (_checklistCamera == null) return;
        RestartCapture();
    }

    /// <summary>
    /// True while this page's checklist camera has an open capture window. ExamNotebook polls
    /// this (rather than yielding on the underlying Coroutine handle directly, which would hang
    /// forever if this page were disabled mid-capture — Unity silently kills the coroutine
    /// without ever resolving a handle someone else is yielding on) to know when it's safe to
    /// start the next page's capture in a sequenced batch.
    /// </summary>
    public bool IsCapturing => _snapshotCoroutine != null;

    /// <summary>
    /// StopCoroutine aborts the coroutine immediately — any code after a "yield return"
    /// (including the restore step that un-hides peers requested by BeginSnapshot) never runs.
    /// EndSnapshot is called explicitly here so an interrupted capture can never permanently
    /// strand a peer's checklist renderers in the hidden state. Every page calls this twice
    /// during initialization (RefreshLockStates, then ApplyBitmask), so this interruption path
    /// runs routinely, not just on edge cases.
    /// </summary>
    private void RestartCapture()
    {
        if (_snapshotCoroutine != null)
        {
            StopCoroutine(_snapshotCoroutine);
            _snapshotCoroutine = null;
            EndSnapshot();
        }

        _snapshotCoroutine = StartCoroutine(SnapshotRoutine());
    }

    private System.Collections.IEnumerator SnapshotRoutine()
    {
        BeginSnapshot();
        _checklistCamera.gameObject.SetActive(true);
        yield return new WaitForSeconds(_drawAnimationDuration);
        EndSnapshot();
        _snapshotCoroutine = null;
    }

    /// <summary>
    /// Requests that every OTHER active page's checklist renderers be hidden for the duration
    /// of this page's capture, so this page's checklist camera — which culls by a layer shared
    /// across all pages — can only ever see its own content. Pages already mid-snapshot are
    /// skipped so two pages capturing at the same instant never blind each other.
    /// </summary>
    private void BeginSnapshot()
    {
        _isSnapshotting = true;
        _peersHiddenByThisSnapshot.Clear();

        for (int i = 0; i < _activePages.Count; i++)
        {
            ExamPage peer = _activePages[i];
            if (peer == null || peer == this || peer._isSnapshotting) continue;

            peer.RequestHideChecklistRenderers();
            _peersHiddenByThisSnapshot.Add(peer);
        }
    }

    /// <summary>
    /// Turns this page's checklist camera off and releases every peer hide request this
    /// snapshot made. Called both on normal completion and when a new snapshot interrupts an
    /// in-flight one, so a request/release pair is guaranteed regardless of coroutine lifetime.
    /// </summary>
    private void EndSnapshot()
    {
        if (_checklistCamera != null)
            _checklistCamera.gameObject.SetActive(false);

        for (int i = 0; i < _peersHiddenByThisSnapshot.Count; i++)
        {
            if (_peersHiddenByThisSnapshot[i] != null)
                _peersHiddenByThisSnapshot[i].ReleaseHideChecklistRenderers();
        }
        _peersHiddenByThisSnapshot.Clear();

        _isSnapshotting = false;
    }

    /// <summary>
    /// Reference-counted: caches each renderer's real enabled state and disables it only on the
    /// first request, so nested/overlapping requests from multiple simultaneous peers can never
    /// restore visibility before every requester has released. Never touches colliders or
    /// transforms, so checkbox click raycasting is unaffected.
    /// </summary>
    private void RequestHideChecklistRenderers()
    {
        if (_hideRequestCount == 0)
        {
            _checklistRenderersCache = checklistItemsParent != null
                ? checklistItemsParent.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
            _preHideRendererEnabledStates = new bool[_checklistRenderersCache.Length];

            for (int i = 0; i < _checklistRenderersCache.Length; i++)
            {
                if (_checklistRenderersCache[i] == null) continue;
                _preHideRendererEnabledStates[i] = _checklistRenderersCache[i].enabled;
                _checklistRenderersCache[i].enabled = false;
            }
        }

        _hideRequestCount++;
    }

    /// <summary>Releases one hide request; only restores renderers once every requester has released.</summary>
    private void ReleaseHideChecklistRenderers()
    {
        if (_hideRequestCount <= 0) return;

        _hideRequestCount--;
        if (_hideRequestCount > 0 || _checklistRenderersCache == null) return;

        for (int i = 0; i < _checklistRenderersCache.Length; i++)
        {
            if (_checklistRenderersCache[i] != null)
                _checklistRenderersCache[i].enabled = _preHideRendererEnabledStates[i];
        }

        _checklistRenderersCache = null;
        _preHideRendererEnabledStates = null;
    }

    private void OnEnable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked += OnAnomalyUnlocked;
        _activePages.Add(this);
    }

    private void OnDisable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked -= OnAnomalyUnlocked;
        _activePages.Remove(this);

        // If this page was mid-snapshot when disabled, release any peers it was hiding so they
        // don't get stranded. If other pages currently have THIS page hidden, leave that alone —
        // their own EndSnapshot will release it, and this page is inactive/invisible anyway.
        if (_snapshotCoroutine != null)
        {
            _snapshotCoroutine = null;
            EndSnapshot();
        }
    }

    private void OnAnomalyUnlocked(string typeName)
    {
        // Re-evaluate all lock states and re-sort; at least one item may have changed.
        RefreshLockStates();
    }

    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }

        if (_paperMaterialInstance != null)
            Destroy(_paperMaterialInstance);
    }

    // ── Lock-state management ─────────────────────────────────────────────────

    /// <summary>
    /// Queries <see cref="AnomalyUnlockManager"/> for every checklist item, applies the
    /// locked/unlocked visual state, re-sorts positions so locked items appear at the bottom,
    /// and re-applies the current exam interactable state so newly-unlocked items become
    /// clickable immediately when the exam is already active.
    /// Called on network spawn and whenever <see cref="AnomalyUnlockManager.OnAnomalyUnlocked"/> fires.
    /// </summary>
    public void RefreshLockStates()
    {
        if (_checklistItems == null || _checklistItems.Length == 0) return;

        bool[] lockedStates = new bool[_checklistItems.Length];
        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (_checklistItems[i] == null) continue;

            bool locked = AnomalyUnlockManager.Instance != null
                && !AnomalyUnlockManager.Instance.IsAnomalyUnlocked(_checklistItems[i].AnomalyTypeName);

            lockedStates[i] = locked;
            _checklistItems[i].ApplyLockState(locked);
        }

        SortChecklistByLockState(lockedStates);

        // Re-sync interactable state so newly-unlocked items can be clicked
        // if the exam is already open, and locked items remain blocked.
        // Use _checklistInteractable (last value set by ApplyCurrentPage) rather than
        // IsChecking, which is only true during the 0.5-second IK arm animation.
        SetChecklistInteractable(_checklistInteractable);

        SnapshotChecklist();
    }

    /// <summary>
    /// Redistributes checklist item positions so unlocked items occupy the top slots
    /// and locked items occupy the bottom slots, preserving relative order within each group.
    /// Uses <see cref="_originalItemPositions"/> as the authoritative slot pool so repeated
    /// calls always produce a consistent layout.
    /// </summary>
    private void SortChecklistByLockState(bool[] lockedStates)
    {
        if (_originalItemPositions == null || _originalItemPositions.Length != _checklistItems.Length)
            return;

        // Collect slot Y values from the original positions, sorted top-to-bottom (descending —
        // confirmed empirically: higher local Y renders higher on the printed page).
        float[] slotYValues = new float[_checklistItems.Length];
        for (int i = 0; i < _checklistItems.Length; i++)
            slotYValues[i] = _originalItemPositions[i].y;

        System.Array.Sort(slotYValues, (a, b) => b.CompareTo(a));

        // Build ordered lists: unlocked items first (preserving their original relative order),
        // then locked items.
        var unlockedIndices = new List<int>(_checklistItems.Length);
        var lockedIndices   = new List<int>(_checklistItems.Length);

        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (lockedStates[i]) lockedIndices.Add(i);
            else unlockedIndices.Add(i);
        }

        // Assign slots: unlocked get the top positions, locked get the bottom positions.
        int slot = 0;
        foreach (int idx in unlockedIndices)
            AssignSlotY(idx, slotYValues[slot++]);
        foreach (int idx in lockedIndices)
            AssignSlotY(idx, slotYValues[slot++]);
    }

    private void AssignSlotY(int itemIndex, float y)
    {
        if (_checklistItems[itemIndex] == null) return;
        Vector3 pos = _checklistItems[itemIndex].transform.localPosition;
        pos.y = y;
        _checklistItems[itemIndex].transform.localPosition = pos;
    }

    // ─────────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()    {
        base.OnNetworkSpawn();

        // PickableObject.OnNetworkSpawn calls SetInteractable(true) when no one holds the object,
        // which would re-enable the page's collider after ExamNotebook.Awake disabled it —
        // causing a race where the page could be picked up instead of the notebook.
        // Guard: a page that hasn't been ripped out is never independently interactable.
        if (!isRippedOut)
            SetInteractable(false);
    }

    public ChecklistItem[] ChecklistItems => _checklistItems;

    public void Initialize(ExamNotebook notebook)
    {
        this.notebook = notebook;
    }

    /// <summary>
    /// Called by ExamNotebook.OnNetworkSpawn to assign each checklist item its array index
    /// and record which page slot this page occupies within the notebook.
    /// Also applies initial lock states and sorts items so locked rows appear at the bottom.
    /// </summary>
    public void InitializeChecklistIndices()
    {
        if (_checklistItems == null) return;

        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (_checklistItems[i] != null)
                _checklistItems[i].SetIndex(i);
            else
                Debug.LogWarning($"[ExamPage] InitializeChecklistIndices: _checklistItems[{i}] is null on '{name}'. Check the prefab's serialized array for missing references.");
        }

        RefreshLockStates();
    }

    /// <summary>Sets which page slot this page occupies, so clicks reference the correct bitmask.</summary>
    public void SetPageIndex(int index) => pageIndex = index;

    /// <summary>
    /// Applies an authoritative bitmask to all checklist items on this page.
    /// Drives both the physical checkbox visuals and the camera-rendered visual items.
    /// Called by ExamNotebook whenever the server writes the NetworkVariable for this page.
    /// </summary>
    public void ApplyBitmask(int bitmask)
    {
        for (int i = 0; i < _checklistItems.Length; i++)
        {
            if (_checklistItems[i] == null)
            {
                Debug.LogWarning($"[ExamPage] ApplyBitmask: _checklistItems[{i}] is null on '{name}'. Check the prefab's serialized array for missing references.");
                continue;
            }

            bool isChecked = (bitmask & (1 << i)) != 0;
            _checklistItems[i].ApplyCheckedState(isChecked);

            if (_visualItems != null && i < _visualItems.Length && _visualItems[i] != null)
                _visualItems[i].SetChecked(isChecked);
        }

        SnapshotChecklist();
    }

    /// <summary>
    /// Called by ChecklistItem when the local player clicks a checkbox.
    /// Routes through the notebook's NetworkObject since nested NetworkObjects cannot send RPCs.
    /// </summary>
    public void SetCheckboxChecked(int itemIndex, bool value)
    {
        notebook.SetCheckboxChecked(pageIndex, itemIndex, value);
    }

    public void AnimateCheckMark(Transform ikAnimationTarget)
    {
        notebook.AnimateCheckMark(ikAnimationTarget);
    }

    public void SetChecklistInteractable(bool b)
    {
        _checklistInteractable = b;
        if (_checklistItems == null) return;

        foreach (ChecklistItem item in _checklistItems)
        {
            if (item == null) continue;
            item.SetInteractable(b);
        }
    }

    /// <summary>
    /// Blocks interaction when this page is slotted inside a folder that another player
    /// is currently holding. Uses the SocketFollow target to locate the owning FolderController
    /// without relying on the non-networked insideThisFolder field, so the guard works correctly
    /// on every client.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        SocketFollow socketFollow = GetComponent<SocketFollow>();
        if (socketFollow != null && socketFollow.Target != null)
        {
            FolderController folder = socketFollow.Target.GetComponentInParent<FolderController>();
            if (folder != null && folder.IsPageHeldByAnotherPlayer(ItemData.name))
                return;
        }

        base.Interact(player);
    }

    /// <summary>
    /// While filed in a folder, keeps the page's physical collider a trigger whenever it is
    /// enabled (i.e. the folder is open) so the page doesn't collide with the folder body
    /// itself and can still be lifted back out. When the folder is closed, FolderItem's guard
    /// keeps this from re-enabling, and the base call below fully disables the collider.
    /// Only the collider(s) on this root GameObject are affected — the child raycast marker
    /// collider (<see cref="InteractableCollider"/> on "Plane.002") is already a permanent
    /// trigger and is untouched here.
    /// </summary>
    public override void SetInteractable(bool value)
    {
        base.SetInteractable(value);

        if (insideThisFolder == null) return;

        bool blockedByFolder = value && (insideThisFolder.IsHeld || !insideThisFolder.IsOpen);
        if (blockedByFolder) return;

        foreach (Collider col in GetComponents<Collider>())
        {
            col.isTrigger = value;
        }
    }
}
