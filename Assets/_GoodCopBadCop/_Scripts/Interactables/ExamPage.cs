using Unity.Netcode;
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
    /// Visual-side checklist items inside Exam Notebook Contents, captured by the
    /// checklist camera and composited onto the paper via _OverlayMap.
    /// Must be indexed in the same order as _checklistItems.
    /// </summary>
    [SerializeField] private ChecklistVisual[] _visualItems;

    /// <summary>The orthographic camera inside Exam Notebook Contents that renders to the RT.</summary>
    [SerializeField] private Camera _checklistCamera;

    /// <summary>The MeshRenderer on Plane.002 whose material exposes _OverlayMap.</summary>
    [SerializeField] private MeshRenderer _paperRenderer;

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
    // ─────────────────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        SetupRenderTexture();
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
            name = $"ChecklistRT_{GetInstanceID()}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        _renderTexture.Create();

        _checklistCamera.targetTexture = _renderTexture;

        // Create a per-instance material so pages don't share the same texture slot.
        // _OverlayMap_ST (tiling/offset) is intentionally inherited from the shared material.
        _paperMaterialInstance = new Material(_paperRenderer.sharedMaterials[0]);
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
    /// </summary>
    private void SnapshotChecklist()
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

    public override void OnNetworkSpawn()
    {
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
    /// </summary>
    public void InitializeChecklistIndices()
    {
        for (int i = 0; i < _checklistItems.Length; i++)
            _checklistItems[i].SetIndex(i);
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
