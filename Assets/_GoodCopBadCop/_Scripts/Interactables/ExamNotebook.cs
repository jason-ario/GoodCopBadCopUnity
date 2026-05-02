using System.Collections;
using HighlightPlus;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

public class ExamNotebook : PickableObject
{
    [SerializeField] private ExamPage[] pages;
    bool addingToFolder = false;
    public bool IsChecking { get; set; }

    private int currentPage = 0;

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

    protected override void Awake()
    {
        base.Awake();
        foreach (var page in pages)
        {
            page.SetChecklistInteractable(false);
            page.SetInteractable(false);
            page.Initialize(this);
        }

        pages[currentPage].SetChecklistInteractable(true);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Pages are scene-hierarchy children of this notebook NetworkObject.
        // Their own NetworkTransform would independently interpolate world-space position,
        // causing them to lag behind the notebook on non-server clients. Disable it here —
        // it will be left disabled by PlaceInSlotServerRpc when a page is ripped out.
        foreach (var page in pages)
        {
            NetworkTransform nt = page.GetComponent<NetworkTransform>();
            if (nt != null) nt.enabled = false;
        }

        // Map the fixed fields into an indexed array for easy access.
        _pageBitmasks = new NetworkVariable<int>[] { _pageBitmask0, _pageBitmask1, _pageBitmask2, _pageBitmask3, _pageBitmask4 };

        // Assign each checklist item its index within its page, and subscribe to bitmask changes.
        for (int p = 0; p < pages.Length && p < _pageBitmasks.Length; p++)
        {
            int capturedPage = p;
            pages[p].SetPageIndex(p);
            pages[p].InitializeChecklistIndices();

            // Apply current value immediately for late joiners.
            pages[capturedPage].ApplyBitmask(_pageBitmasks[capturedPage].Value);

            _pageBitmasks[p].OnValueChanged += (_, newValue) =>
                pages[capturedPage].ApplyBitmask(newValue);
        }
    }

    /// <summary>
    /// Called by ExamPage when the local player clicks a checkbox.
    /// Routes through the notebook's ServerRpc since nested NetworkObjects can't send RPCs.
    /// </summary>
    public void SetCheckboxChecked(int pageIndex, int itemIndex, bool value)
    {
        SetCheckboxServerRpc(pageIndex, itemIndex, value);
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
        playerPickupController.PlayerAnimationController.RightArmRigIKTarget = ikAnimationTarget;
        StartCoroutine(TurnRightArmRigOff());
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

        foreach (var itemHeld in player.RightArmCamObjectContainer.ItemsHeld)
        {
            if (itemHeld.ItemData.name == "RedPencil")
            {
                itemHeld.gameObject.SetActive(true);
            }
        }
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);
        foreach (var itemHeld in player.RightArmCamObjectContainer.ItemsHeld)
        {
            if (itemHeld.ItemData.name == "RedPencil")
            {
                itemHeld.gameObject.SetActive(false);
            }
        }
    }

    public override void OnStartUse()
    {
        if (addingToFolder)
        {
            return;
        }

        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        playerPickupController.CanPickUpAndPlace = false;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(false);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(false);
        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(ExitDrawMode);
    }

    void ExitDrawMode()
    {
        playerPickupController.CanPickUpAndPlace = true;
        playerPickupController.GetComponent<PlayerMovementController>().SetCanControl(true);
        playerPickupController.GetComponent<PlayerMovementController>().SetCanMove(true);
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();
    }

    public void AddToFolder(FolderController folder)
    {
        addingToFolder = true;
        pages[currentPage].SetChecklistInteractable(false);

        playerPickupController.PlayerAnimationController.SetAnimTrigger("RipOutPage");
        StartCoroutine(WaitAndParent(pages[currentPage], folder));
        Debug.Log("Rip out and add to folder");
    }

    IEnumerator WaitAndParent(ExamPage rippedPage, FolderController folder)
    {
        yield return new WaitForSeconds(.5f);
        rippedPage.pageAnimator.SetTrigger("RipOut");
        yield return new WaitForSeconds(.4f);

        // Place the page directly into the folder slot via the network-safe path.
        // FolderController.AddDocument (dropObject=false) calls PlaceInSlotServerRpc so
        // NT is disabled and all clients register the document in LateUpdate.
        folder.AddDocument(rippedPage, playerPickupController, false);

        GetComponent<HighlightEffect>().SetupMaterial();
        rippedPage.pageAnimator.SetTrigger("Reset");
        currentPage += 1;
        pages[currentPage].SetChecklistInteractable(true);
        addingToFolder = false;
    }
}
