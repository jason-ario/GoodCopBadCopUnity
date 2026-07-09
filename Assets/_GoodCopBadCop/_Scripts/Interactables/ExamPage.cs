using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine;

public class ExamPage : FolderItem
{
    [SerializeField] private ChecklistItem[] _checklistItems;
    private ExamNotebook notebook;
    private int pageIndex;
    public Animator pageAnimator;
    public bool IsChecking => notebook.IsChecking;
    public bool isRippedOut;
    [SerializeField] private GameObject container;

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
    /// Should cover the full X drawing animation length (~0.52 s) plus a small margin.
    /// </summary>
    [SerializeField] private float _drawAnimationDuration = 0.6f;

    private static readonly int OverlayMapProperty = Shader.PropertyToID("_OverlayMap");

    private RenderTexture _renderTexture;
    private Material _paperMaterialInstance;
    private Coroutine _snapshotCoroutine;

    /// <summary>
    /// Original local positions of each checklist item captured at Awake.
    /// Used as the position pool when re-sorting by lock state so repeated
    /// calls to RefreshLockStates always produce a consistent result.
    /// </summary>
    private Vector3[] _originalItemPositions;
    // ─────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        CacheVisualItems();
        CacheOriginalItemPositions();
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
            return;

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
    public void SnapshotChecklist()
    {
        if (_checklistCamera == null) return;

        if (_snapshotCoroutine != null)
            StopCoroutine(_snapshotCoroutine);
        _snapshotCoroutine = StartCoroutine(SnapshotRoutine());
    }

    private System.Collections.IEnumerator SnapshotRoutine()
    {
        _checklistCamera.gameObject.SetActive(true);
        yield return new WaitForSeconds(_drawAnimationDuration);
        _checklistCamera.gameObject.SetActive(false);
        _snapshotCoroutine = null;
    }

    private void OnEnable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked += OnAnomalyUnlocked;
    }

    private void OnDisable()
    {
        AnomalyUnlockManager.OnAnomalyUnlocked -= OnAnomalyUnlocked;
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
        SetChecklistInteractable(IsChecking);

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

        // Collect slot Y values from the original positions, sorted top-to-bottom (descending).
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
        foreach (ChecklistItem item in _checklistItems)
            item.SetInteractable(b);
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
}
