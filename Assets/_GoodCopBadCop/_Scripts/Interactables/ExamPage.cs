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
    /// Called by ExamNotebook whenever the server writes the NetworkVariable for this page.
    /// </summary>
    public void ApplyBitmask(int bitmask)
    {
        for (int i = 0; i < _checklistItems.Length; i++)
            _checklistItems[i].ApplyCheckedState((bitmask & (1 << i)) != 0);
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
