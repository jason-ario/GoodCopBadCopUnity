using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// A <see cref="PickableObject"/> that stores any item in a FIFO queue.
///
/// Interaction summary:
/// - LMB empty-handed             → pick up the backpack (standard).
/// - LMB while holding an item    → store that item in the backpack (IHeldItemPassthrough).
/// - E  empty-handed in world     → extract the oldest stored item into your hands.
/// - LMB while holding backpack   → equip to player's back (OnStartUse).
/// - Unequip key (G) while worn   → unequip and hold in hands (both hands must be free).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BackpackPickable : PickableObject, IHeldItemPassthrough
{
    private const KeyCode UnequipKey = KeyCode.G;
    private const string InteractTextEmpty  = "Backpack";
    private const string InteractTextFormat = "Backpack ({0} stored)";

    [Header("Backpack Audio")]
    [SerializeField] private AudioClip _addItemSound;
    [SerializeField] private AudioClip _extractSound;
    [SerializeField] private AudioClip _equipSound;
    [SerializeField] private AudioClip _unequipSound;

    // ── Networked State ───────────────────────────────────────────────────────

    /// <summary>FIFO queue of items stored in this specific backpack instance.</summary>
    private NetworkList<NetworkObjectReference> _storedItems = new NetworkList<NetworkObjectReference>();

    /// <summary>
    /// Client ID of the player currently wearing this backpack.
    /// ulong.MaxValue when not equipped.
    /// </summary>
    private readonly NetworkVariable<ulong> _wearingPlayerId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsEquipped       => _wearingPlayerId.Value != ulong.MaxValue;
    public ulong WearingPlayerId => _wearingPlayerId.Value;
    public int  StoredItemCount  => _storedItems?.Count ?? 0;

    /// <summary>
    /// Clears the worn state before the base recovery path returns this backpack to the world.
    /// This also restores the wearer's character mesh visibility through the replicated state.
    /// </summary>
    public override void ForceReleaseToWorldServer(Vector3 position, Quaternion rotation)
    {
        if (IsServer && _wearingPlayerId.Value != ulong.MaxValue)
            _wearingPlayerId.Value = ulong.MaxValue;

        base.ForceReleaseToWorldServer(position, rotation);
    }


    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        var storedIds = new System.Collections.Generic.List<string>(_storedItems.Count);
        foreach (NetworkObjectReference itemRef in _storedItems)
        {
            if (!itemRef.TryGet(out NetworkObject itemNetworkObject)) continue;
            PickableObject item = itemNetworkObject.GetComponent<PickableObject>();
            if (item != null)
                storedIds.Add(item.SaveId);
        }

        data.StringState = storedIds.ToArray();
    }

    /// <summary>
    /// Server-only: rebuilds this backpack's FIFO membership after all world pickables have
    /// registered. Stored items stay hidden, non-interactable, and parent-constrained locally.
    /// </summary>
    public void RestoreStoredItemsServer(string[] savedItemIds)
    {
        if (!IsServer || savedItemIds == null) return;

        _storedItems.Clear();
        foreach (string itemId in savedItemIds)
        {
            if (!PickableObjectRegistry.Instance.TryGetPickable(itemId, out PickableObject item) || item == null || item == this)
                continue;

            NetworkObject itemNetworkObject = item.NetworkObject;
            if (itemNetworkObject == null || !itemNetworkObject.IsSpawned)
                continue;

            item.ForceReleaseToWorldServer();
            itemNetworkObject.RemoveOwnership();
            NetworkTransform itemTransform = itemNetworkObject.GetComponent<NetworkTransform>();
            if (itemTransform != null) itemTransform.enabled = false;
            item.SetInteractableNetworked(false);
            _storedItems.Add(new NetworkObjectReference(itemNetworkObject));
            StoreRestoredItemClientRpc(new NetworkObjectReference(itemNetworkObject));
        }
    }

    [ClientRpc]
    private void StoreRestoredItemClientRpc(NetworkObjectReference itemRef)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetworkObject)) return;
        PickableObject item = itemNetworkObject.GetComponent<PickableObject>();
        if (item == null) return;

        Rigidbody rb = itemNetworkObject.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;
        NetworkTransform itemTransform = itemNetworkObject.GetComponent<NetworkTransform>();
        if (itemTransform != null) itemTransform.enabled = false;
        item.NetworkObject.AutoObjectParentSync = false;
        item.transform.position = transform.position;
        item.transform.rotation = transform.rotation;
        item.SetParent(transform);
        ApplyStoredItemVisuals(itemRef);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractTextEmpty;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _wearingPlayerId.OnValueChanged += OnWearingPlayerChanged;
        _storedItems.OnListChanged      += OnStoredItemsChanged;

        // Late-joiner sync
        UpdateInteractText();
        if (IsEquipped)
            ApplyEquippedVisuals(_wearingPlayerId.Value);
        foreach (var itemRef in _storedItems)
            ApplyStoredItemVisuals(itemRef);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _wearingPlayerId.OnValueChanged -= OnWearingPlayerChanged;
        _storedItems.OnListChanged      -= OnStoredItemsChanged;
    }

    private void Update()
    {
        // Only the wearing player may unequip
        if (!IsSpawned) return;
        if (_wearingPlayerId.Value == ulong.MaxValue) return;
        if (_wearingPlayerId.Value != NetworkManager.Singleton.LocalClientId) return;
        if (Input.GetKeyDown(UnequipKey))
            TryUnequip();
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// LMB empty-handed → standard pickup via base class.
    /// LMB while holding an item (IHeldItemPassthrough path) → store held item.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        PickableObject heldItem = player.pickupController.HeldObject;
        if (heldItem != null)
        {
            if (heldItem == this) return;
            AddItemServerRpc(new NetworkObjectReference(heldItem.NetworkObject));
        }
        else
        {
            base.Interact(player);
        }
    }

    /// <summary>
    /// E or LMB (empty-handed) → extract the oldest stored item into the player's hands,
    /// or pick up the backpack itself when it is empty.
    /// </summary>
    public override void InteractAlternate(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject != null) return;
        if (_storedItems.Count == 0)
        {
            // Nothing to extract — fall back to a standard pickup.
            base.InteractAlternate(player);
            return;
        }
        ExtractItemServerRpc();
    }

    /// <summary>LMB while held → equip backpack to player's back.</summary>
    public override void OnStartUse()
    {
        base.OnStartUse();
        if (!IsOwner) return;
        EquipBackpackServerRpc();
    }

    /// <summary>
    /// Suppress the base SetInteractable(true) call that OnUnequip triggers
    /// while the backpack is in the process of being equipped (owned by server).
    /// Interactability is restored via the networked override when unequipped.
    /// </summary>
    public override void OnUnequip(PlayerPickupController player)
    {
        base.OnUnequip(player);
        if (IsEquipped)
            SetInteractable(false);
    }

    // ── Add Item ──────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void AddItemServerRpc(NetworkObjectReference itemRef, ServerRpcParams rpcParams = default)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetObj))
        {
            Debug.LogWarning("[BackpackPickable] AddItemServerRpc: could not resolve item NetworkObject.");
            return;
        }

        PickableObject item = itemNetObj.GetComponent<PickableObject>();
        if (item == null) return;

        _storedItems.Add(itemRef);

        // Return ownership to server; the backpack manages the item's position via constraint.
        itemNetObj.RemoveOwnership();

        // Disable NT — position is driven by the ParentConstraint to the backpack.
        NetworkTransform nt = itemNetObj.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        // Prevent anyone else from picking this item up while it is stored.
        item.SetInteractableNetworked(false);

        if (_addItemSound != null)
            SFXController.Instance.PlayAtPosition(_addItemSound, transform.position);

        StoreItemClientRpc(itemRef, rpcParams.Receive.SenderClientId);
    }

    /// <summary>
    /// Broadcast: hide and constrain the item to this backpack on all clients.
    /// On the holding player's client the grip is also released.
    /// </summary>
    [ClientRpc]
    private void StoreItemClientRpc(NetworkObjectReference itemRef, ulong holderClientId)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetObj)) return;

        PickableObject item = itemNetObj.GetComponent<PickableObject>();
        if (item == null) return;

        // The holding player releases grip without a world drop.
        if (NetworkManager.Singleton.LocalClientId == holderClientId)
        {
            PlayerPickupController ppc = GetLocalPlayerPickupController();
            ppc?.ReleaseObjectToBackpack();
        }

        // Physics + NT off — backpack constraint drives position.
        Rigidbody rb = itemNetObj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = true;

        NetworkTransform nt = itemNetObj.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        item.NetworkObject.AutoObjectParentSync = false;
        item.transform.position = transform.position;
        item.transform.rotation = transform.rotation;
        item.SetParent(transform); // SetParent calls RemoveParent internally

        ApplyStoredItemVisuals(itemRef);
    }

    /// <summary>Hides all renderers on the stored item — called both at runtime and for late joiners.</summary>
    private void ApplyStoredItemVisuals(NetworkObjectReference itemRef)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetObj)) return;
        foreach (var rend in itemNetObj.GetComponentsInChildren<Renderer>(true))
            rend.enabled = false;
    }

    // ── Extract Item ──────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ExtractItemServerRpc(ServerRpcParams rpcParams = default)
    {
        if (_storedItems.Count == 0) return;

        NetworkObjectReference itemRef = _storedItems[0];
        _storedItems.RemoveAt(0);

        if (!itemRef.TryGet(out NetworkObject itemNetObj))
        {
            Debug.LogWarning("[BackpackPickable] ExtractItemServerRpc: stale item reference removed.");
            return;
        }

        PickableObject item = itemNetObj.GetComponent<PickableObject>();
        // Restore normal holder-based interactable logic before pickup.
        item?.UnlockInteractableNetworked();

        if (_extractSound != null)
            SFXController.Instance.PlayAtPosition(_extractSound, transform.position);

        ExtractItemClientRpc(itemRef, rpcParams.Receive.SenderClientId);
    }

    [ClientRpc]
    private void ExtractItemClientRpc(NetworkObjectReference itemRef, ulong receiverClientId)
    {
        if (!itemRef.TryGet(out NetworkObject itemNetObj)) return;

        PickableObject item = itemNetObj.GetComponent<PickableObject>();
        if (item == null) return;

        // Restore full physics and network state before pickup.
        item.RemoveParent();
        item.ClearSocketFollow();
        item.NetworkObject.AutoObjectParentSync = true;

        Rigidbody rb = itemNetObj.GetComponent<Rigidbody>();
        if (rb != null) rb.isKinematic = false;

        NetworkTransform nt = itemNetObj.GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;

        foreach (var rend in itemNetObj.GetComponentsInChildren<Renderer>(true))
            rend.enabled = true;

        // Hand the item directly to the requesting player.
        if (NetworkManager.Singleton.LocalClientId != receiverClientId) return;

        PlayerPickupController ppc = GetLocalPlayerPickupController();
        if (ppc == null) return;

        if (ppc.holdPoint != null)
        {
            item.transform.position = ppc.holdPoint.position;
            item.transform.rotation = ppc.holdPoint.rotation;
        }

        ppc.PickUpObject(item);
    }

    // ── Equip ─────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void EquipBackpackServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        _wearingPlayerId.Value = clientId;

        // Release the player's grip — they no longer carry it in their hands.
        ReleaseHolderServerRpc();
        NetworkObject.RemoveOwnership();

        // Disable NT; the constraint to the player's back anchor drives position.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        // Keep non-interactable while worn.
        SetInteractableNetworked(false);

        if (_equipSound != null)
            SFXController.Instance.PlayAtPosition(_equipSound, transform.position);

        // Tell the wearing player to clear pickup state.
        ReleaseGripClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    /// <summary>Targeted to the wearing player: clears pickup state without a world drop.</summary>
    [ClientRpc]
    private void ReleaseGripClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = GetLocalPlayerPickupController();
        ppc?.ReleaseObjectToBackpack();
        // Visual/constraint changes are driven by _wearingPlayerId.OnValueChanged.
    }

    // ── Unequip ───────────────────────────────────────────────────────────────

    private void TryUnequip()
    {
        PlayerPickupController ppc = GetLocalPlayerPickupController();
        if (ppc == null || ppc.IsHoldingObject) return;
        UnequipBackpackServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void UnequipBackpackServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (_wearingPlayerId.Value != clientId) return;

        _wearingPlayerId.Value = ulong.MaxValue;

        // Restore holder-based interactable logic; PickUpObject will claim the holder.
        UnlockInteractableNetworked();

        // Transfer ownership so the player can immediately pick it up.
        NetworkObject.ChangeOwnership(clientId);

        if (_unequipSound != null)
            SFXController.Instance.PlayAtPosition(_unequipSound, transform.position);

        PickUpAfterUnequipClientRpc(new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    /// <summary>Targeted to the unequipping player: removes constraint and picks it up.</summary>
    [ClientRpc]
    private void PickUpAfterUnequipClientRpc(ClientRpcParams clientRpcParams = default)
    {
        PlayerPickupController ppc = GetLocalPlayerPickupController();
        if (ppc == null) return;

        RemoveParent();

        if (ppc.holdPoint != null)
        {
            transform.position = ppc.holdPoint.position;
            transform.rotation = ppc.holdPoint.rotation;
        }

        ppc.PickUpObject(this);
    }

    // ── Equipped Visuals ──────────────────────────────────────────────────────

    private void OnWearingPlayerChanged(ulong previous, ulong current)
    {
        if (current != ulong.MaxValue)
            ApplyEquippedVisuals(current);
        else if (previous != ulong.MaxValue)
            RemoveEquippedVisuals(previous);
    }

    private void ApplyEquippedVisuals(ulong wearingClientId)
    {
        SetBackpackWorldVisible(false);

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(wearingClientId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerEquipmentController equipment = client.PlayerObject.GetComponent<PlayerEquipmentController>();
        equipment?.ShowBackpackMesh(true);

        // Constrain the backpack world object to the player's back anchor (if configured).
        Transform anchor = equipment?.BackpackAnchor;
        if (anchor == null) return;

        NetworkObject.AutoObjectParentSync = false;
        transform.position = anchor.position;
        transform.rotation = anchor.rotation;
        SetParent(anchor);
    }

    private void RemoveEquippedVisuals(ulong previousWearerId)
    {
        SetBackpackWorldVisible(true);
        RemoveParent();
        NetworkObject.AutoObjectParentSync = true;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(previousWearerId, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        PlayerEquipmentController equipment = client.PlayerObject.GetComponent<PlayerEquipmentController>();
        equipment?.ShowBackpackMesh(false);
    }

    private void SetBackpackWorldVisible(bool visible)
    {
        foreach (var rend in GetComponentsInChildren<Renderer>(true))
            rend.enabled = visible;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void UpdateInteractText()
    {
        interactText = _storedItems.Count > 0
            ? string.Format(InteractTextFormat, _storedItems.Count)
            : InteractTextEmpty;
    }

    private void OnStoredItemsChanged(NetworkListEvent<NetworkObjectReference> changeEvent)
        => UpdateInteractText();

    private static PlayerPickupController GetLocalPlayerPickupController()
        => NetworkManager.Singleton.LocalClient?.PlayerObject?.GetComponent<PlayerPickupController>();
}
