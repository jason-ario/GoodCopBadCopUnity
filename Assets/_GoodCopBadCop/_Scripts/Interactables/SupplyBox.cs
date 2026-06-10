using System.Collections.Generic;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

public class SupplyBox : PickableObject
{
    public bool canPickUp = false;
    [SerializeField] private Animator _animator;
    [SerializeField] private GameObject contents;
    bool isOpen = false;

    private static readonly int BoxOpenHash = Animator.StringToHash("BoxOpen");

    /// <summary>
    /// Items spawned into this box. Once an item is picked up by a player it is
    /// removed from this list so closing the box never re-locks it.
    /// </summary>
    private readonly List<PickableObject> _registeredItems = new List<PickableObject>();

    /// <summary>Networked authoritative state of <see cref="canPickUp"/>. Synced to all clients.</summary>
    private NetworkVariable<bool> _networkCanPickUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Parent transform used to attach per-day items during delivery. Falls back to this transform if contents is unassigned.</summary>
    public Transform ContentsParent => contents != null ? contents.transform : transform;

    // ── Network Lifecycle ─────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _networkCanPickUp.OnValueChanged += OnNetworkCanPickUpChanged;
        canPickUp = _networkCanPickUp.Value;
        UpdateInteractText();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkCanPickUp.OnValueChanged -= OnNetworkCanPickUpChanged;
    }

    private void OnNetworkCanPickUpChanged(bool previous, bool current)
    {
        canPickUp = current;
        UpdateInteractText();
    }

    private void UpdateInteractText()
    {
        if (!isOpen)
            interactText = "Open Box [E]";
        else
            interactText = canPickUp ? "Close Box [E] | Pick Up [LMB]" : "Close Box [E]";
    }

    // ── Item Registration ─────────────────────────────────────────────────────

    /// <summary>
    /// Registers a spawned item so the box can manage its interactability.
    /// When the item is picked up by a player it automatically unregisters itself
    /// via <see cref="UnregisterItemServerRpc"/> so closing the box never re-locks it.
    /// </summary>
    public void RegisterItem(PickableObject item)
    {
        if (item == null || _registeredItems.Contains(item)) return;
        _registeredItems.Add(item);

        // Once the item is equipped by a player, release it from box management.
        item.OnEquip += () =>
        {
            _registeredItems.Remove(item); // local removal (client that did the pickup)
            if (item.IsSpawned)
                UnregisterItemServerRpc(new NetworkObjectReference(item.NetworkObject));
        };
    }

    /// <summary>Removes an item from the managed list on the server so close never re-locks it.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void UnregisterItemServerRpc(NetworkObjectReference itemRef)
    {
        if (itemRef.TryGet(out NetworkObject netObj) && netObj.TryGetComponent(out PickableObject pickable))
            _registeredItems.Remove(pickable);
    }

    /// <summary>Clears all registered items, e.g. when the box is despawned for a new delivery.</summary>
    public void ClearRegisteredItems() => _registeredItems.Clear();

    // ── Server-Side Item Lock Helpers ─────────────────────────────────────────

    /// <summary>Unlocks all registered items so normal holder-based interactability applies.</summary>
    private void UnlockItemsOnServer()
    {
        foreach (PickableObject item in _registeredItems)
            if (item != null) item.UnlockInteractableNetworked();
    }

    /// <summary>Permanently locks all registered items regardless of holder state.</summary>
    private void LockItemsOnServer()
    {
        foreach (PickableObject item in _registeredItems)
            if (item != null) item.LockInteractableNetworked();
    }

    /// <summary>Locks only items that are not currently held by a player.</summary>
    private void LockUnheldItemsOnServer()
    {
        foreach (PickableObject item in _registeredItems)
            if (item != null && !item.IsHeld) item.LockInteractableNetworked();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnlockItemsServerRpc() => UnlockItemsOnServer();

    [ServerRpc(RequireOwnership = false)]
    private void LockItemsServerRpc() => LockItemsOnServer();

    // ── SetCanPickUp ──────────────────────────────────────────────────────────

    /// <summary>Sets <see cref="canPickUp"/> on all clients via the server.</summary>
    public void SetCanPickUpNetworked(bool value)
    {
        if (IsServer)
            _networkCanPickUp.Value = value;
        else
            SetCanPickUpServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetCanPickUpServerRpc(bool value) => _networkCanPickUp.Value = value;

    // ── Delivery RPCs ─────────────────────────────────────────────────────────

    /// <summary>Resets the box to its closed, non-pickable state for a fresh delivery.</summary>
    [ClientRpc]
    public void ResetForDeliveryClientRpc()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, false);
        UpdateInteractText();
    }

    /// <summary>Immediately enables interaction components on all clients, bypassing NetworkVariable latency.</summary>
    [ClientRpc]
    public void FinalizeDeliveryClientRpc()
    {
        SetInteractable(true);
        canPickUp = true;
        UpdateInteractText();
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        // E Key -> Toggle open / closed
        if (Input.GetKeyDown(KeyCode.E))
        {
            if (!isOpen)
                OpenBoxNetworked();
            else
                CloseBoxNetworked();
        }
        // Left Click -> Pick up the box
        else if (Input.GetMouseButtonDown(0))
        {
            if (canPickUp)
                base.Interact(player);
        }
    }

    // ── Open / Close (networked) ──────────────────────────────────────────────

    /// <summary>Unlocks registered items then triggers the open animation on all clients.</summary>
    public void OpenBoxNetworked()
    {
        if (IsServer)
        {
            UnlockItemsOnServer();
            OpenBoxClientRpc();
        }
        else
            OpenBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void OpenBoxServerRpc()
    {
        UnlockItemsOnServer();
        OpenBoxClientRpc();
    }

    [ClientRpc]
    private void OpenBoxClientRpc()
    {
        if (!isOpen)
        {
            OpenBox();
            UpdateInteractText();
        }
    }

    /// <summary>Locks remaining (un-picked-up) items then triggers the close animation on all clients.</summary>
    public void CloseBoxNetworked()
    {
        if (IsServer)
        {
            LockUnheldItemsOnServer();
            CloseBoxClientRpc();
        }
        else
            CloseBoxServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CloseBoxServerRpc()
    {
        LockUnheldItemsOnServer();
        CloseBoxClientRpc();
    }

    [ClientRpc]
    private void CloseBoxClientRpc()
    {
        if (isOpen)
        {
            CloseBox();
            UpdateInteractText();
        }
    }

    // ── SetInteractable ───────────────────────────────────────────────────────

    public override void SetInteractable(bool value)
    {
        base.SetInteractable(value);

        if (TryGetComponent(out BoxCollider boxCollider))
            boxCollider.enabled = value;

        if (TryGetComponent(out HighlightEffect highlight))
            highlight.enabled = value;

        UpdateInteractText();
    }

    // ── Local Visual State ────────────────────────────────────────────────────

    void OpenBox()
    {
        isOpen = true;
        if (contents != null)
            contents.SetActive(true);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, true);
    }

    void CloseBox()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, false);
    }

    // ── Box Pickup / Drop ─────────────────────────────────────────────────────

    public override void OnPickedUp()
    {
        base.OnPickedUp();
        // Lock all registered items while the box is being carried.
        if (IsServer)
            LockItemsOnServer();
        else
            LockItemsServerRpc();
    }

    public override void OnDropped()
    {
        base.OnDropped();
        // Restore item interactability only if the box was open when put down.
        if (!isOpen) return;
        if (IsServer)
            UnlockItemsOnServer();
        else
            UnlockItemsServerRpc();
    }
}
