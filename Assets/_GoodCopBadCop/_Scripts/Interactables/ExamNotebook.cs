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

    protected override void Awake()
    {
        base.Awake();
        foreach (var page in pages)
        {
            page.SetChecklistInteractable(false);
            page.SetInteractable(false);
            page.CanPickUpManually = false;
            page.Initialize(this);
        }

        pages[currentPage].SetChecklistInteractable(true);

        // Remove page-owned InteractableColliders from the notebook's own cache so that
        // PickableObject.SetInteractable (called on equip/unequip) never re-enables them.
        // Each page manages its own collider state independently via SetInteractable.
        ExcludePageCollidersFromCache();
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

        // When the notebook is spawned dynamically at runtime (e.g. purchased from shop),
        // NGO does not spawn nested child NetworkObjects — only scene-placed objects get that.
        // Explicitly spawn each page NetworkObject on the server if it isn't already spawned.
        // We must temporarily detach the page from the notebook hierarchy for Spawn() to succeed,
        // then immediately re-parent it back via TrySetParent so NGO replicates the parent
        // relationship to all clients — the pages will again appear as notebook children everywhere.
        if (IsServer)
        {
            foreach (var page in pages)
            {
                NetworkObject pageNetObj = page.GetComponent<NetworkObject>();
                if (pageNetObj != null && !pageNetObj.IsSpawned)
                {
                    // Temporarily detach so Spawn() registers this as a root-level NetworkObject.
                    page.transform.SetParent(null, worldPositionStays: true);
                    pageNetObj.Spawn(destroyWithScene: true);

                    // Re-parent back to the notebook through NGO so all clients mirror this hierarchy.
                    pageNetObj.TrySetParent(NetworkObject, worldPositionStays: true);
                }
            }
        }

        // Pages are scene-hierarchy children of this notebook NetworkObject (or now detached).
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

        // Sync the active page for late joiners: disable all non-ripped pages' checklists,
        // then enable only the current active page's checklist.
        ApplyCurrentPage(_currentPage.Value);
        _currentPage.OnValueChanged += (_, newValue) => ApplyCurrentPage(newValue);
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
        playerPickupController.PlayerAnimationController.RightArmIKTarget = ikAnimationTarget;
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
                if (itemHeld != null && itemHeld.ItemData.name == RedPencilName)
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

        int pageIndex = System.Array.IndexOf(pages, rippedPage);

        // All three calls below must originate from the server so their ClientRpc broadcasts
        // reach every connected client. A non-host client calling a [ClientRpc] directly only
        // executes it locally — the other clients never see it. Routing through [ServerRpc]
        // wrappers ensures the server is always the one invoking the ClientRpc, regardless of
        // which client initiated the rip-out.
        BroadcastRipOutPageServerRpc(pageIndex);

        yield return new WaitForSeconds(.4f);

        // Place the page directly into the folder slot via the network-safe path.
        // FolderController.AddDocument (dropObject=false) calls PlaceInSlotServerRpc so
        // NT is disabled and all clients register the document in LateUpdate.
        folder.AddDocument(rippedPage, playerPickupController, false);

        AdvancePageServerRpc(pageIndex);

        // SetupMaterial is deferred inside ResetPageClientRpc to ensure PlaceInSlotClientRpc
        // has detached the page from this hierarchy before HighlightEffect scans children.
        BroadcastResetPageServerRpc(pageIndex);
        addingToFolder = false;
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
    /// Waits until the given page's renderers are no longer children of this transform,
    /// then rebuilds HighlightEffect's material list. This is more reliable than a fixed
    /// one-frame delay because PlaceInSlotClientRpc arrives from a different NetworkObject
    /// with no ordering guarantee relative to RPCs on this object.
    /// </summary>
    private IEnumerator RefreshHighlightAfterDetach(ExamPage page)
    {
        // Poll each frame until the page transform is no longer under this hierarchy.
        while (page != null && page.transform.IsChildOf(transform))
        {
            yield return null;
        }

        GetComponent<HighlightEffect>().SetupMaterial();
    }
}
