using System.Collections.Generic;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

public class SupplyBox : PickableObject
{
    public bool canPickUp = false;
    [SerializeField] private Animator _animator;
    [SerializeField] private GameObject contents;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openClip;
    [SerializeField] private AudioClip _closeClip;
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

    /// <summary>
    /// Server-computed empty state replicated to all clients so any player can check
    /// <see cref="IsEmpty"/> without relying on the local, server-only <see cref="_hasHadItems"/>
    /// or <see cref="_registeredItems"/> list.
    /// </summary>
    private NetworkVariable<bool> _networkIsEmpty = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Parent transform used to attach per-day items during delivery. Falls back to this transform if contents is unassigned.</summary>
    public Transform ContentsParent => contents != null ? contents.transform : transform;

    /// <summary>Set to true the first time an item is registered so <see cref="IsEmpty"/> can
    /// distinguish "nothing was ever added" from "everything has been taken".</summary>
    private bool _hasHadItems;

    /// <summary>
    /// Returns true when at least one item was delivered and all of them have since been picked up.
    /// On the server the authoritative local lists are used directly; on clients the value is read
    /// from <see cref="_networkIsEmpty"/> which the server keeps in sync.
    /// </summary>
    public bool IsEmpty => IsServer
        ? (_hasHadItems && _registeredItems.Count == 0)
        : _networkIsEmpty.Value;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        
    }

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
        // Unlock any items that are still registered (e.g. box destroyed while carrying items).
        // Must run before base.OnNetworkDespawn() so item NetworkVariables are still active.
        if (IsServer)
        {
            foreach (PickableObject item in _registeredItems)
            {
                if (item != null && item.IsSpawned)
                    item.UnlockInteractableNetworked();
            }
        }

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
        _hasHadItems = true;
        if (IsServer) _networkIsEmpty.Value = false;

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
        {
            _registeredItems.Remove(pickable);
            _networkIsEmpty.Value = _hasHadItems && _registeredItems.Count == 0;
        }
    }

    /// <summary>Clears all registered items, e.g. when the box is despawned for a new delivery.</summary>
    public void ClearRegisteredItems()
    {
        _registeredItems.Clear();
        _hasHadItems = false;
        if (IsSpawned && IsServer)
            _networkIsEmpty.Value = false;
    }

    // ── Server-Side Item Lock Helpers ─────────────────────────────────────────

    /// <summary>Unlocks all registered items so normal holder-based interactability applies.</summary>
    private void UnlockItemsOnServer()
    {
        foreach (PickableObject item in _registeredItems)
            if (item != null && item.IsSpawned) item.UnlockInteractableNetworked();
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
        if (_audioSource != null && _openClip != null)
            _audioSource.PlayOneShot(_openClip);
    }

    void CloseBox()
    {
        isOpen = false;
        if (contents != null)
            contents.SetActive(false);
        if (_animator != null)
            _animator.SetBool(BoxOpenHash, false);
        if (_audioSource != null && _closeClip != null)
            _audioSource.PlayOneShot(_closeClip);
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
