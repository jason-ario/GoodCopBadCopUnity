using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class InkStamp : Interactable, IPickupSlot
{
    [SerializeField] private PlaceObjectSlot stampPlaceObjectSlot;
    public StampContainer.StampType StampType;
    [SerializeField] private PickableObject inkStampPickup;
    private PickableObject spawnedInkStamp;

    [Header("Ink Label")]
    [SerializeField] private StampInkLabel inkLabel;

    /// <summary>
    /// Tracks the spawned stamp NetworkObject across all clients so that non-host
    /// clients can resolve <see cref="spawnedInkStamp"/> as soon as the value is set,
    /// without relying on ClientRpc ordering relative to OnNetworkSpawn.
    /// </summary>
    private NetworkVariable<NetworkObjectReference> _spawnedStampRef = new NetworkVariable<NetworkObjectReference>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Networked interactability gate for the stamp slot collider.
    /// Defaults to false so the slot starts locked; Day_01 enables it at the stamp beat.
    /// Applied on all clients via OnValueChanged and on late-joiners via OnNetworkSpawn.
    /// </summary>
    private NetworkVariable<bool> _slotInteractable = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Whether the stamp is currently sitting in its slot.
    /// Authoritative on the server; replicated to all clients so every machine
    /// agrees on the placed state before allowing a pickup interaction.
    /// </summary>
    private NetworkVariable<bool> _isPlaced = new NetworkVariable<bool>(
        true,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _spawnedStampRef.OnValueChanged  += OnSpawnedStampRefChanged;
        _slotInteractable.OnValueChanged += OnSlotInteractableChanged;
        _isPlaced.OnValueChanged         += OnIsPlacedChanged;

        // Apply current states for late-joiners.
        ApplySlotInteractable(_slotInteractable.Value);
        stampPlaceObjectSlot.IsPlaced = _isPlaced.Value;

        if (IsServer)
        {
            _isPlaced.Value = true;
            SpawnInkStamp();
        }
        else
        {
            // On clients, the NetworkVariable may already be populated if we joined late.
            TryResolveSpawnedStamp(_spawnedStampRef.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _spawnedStampRef.OnValueChanged  -= OnSpawnedStampRefChanged;
        _slotInteractable.OnValueChanged -= OnSlotInteractableChanged;
        _isPlaced.OnValueChanged         -= OnIsPlacedChanged;
    }

    /// <summary>Shows the ink count label when the player's reticle hovers over this stamp slot.</summary>
    protected override void OnHighlight()
    {
        base.OnHighlight();
        inkLabel?.Show(StampType);
    }

    /// <summary>Hides the ink count label when the reticle leaves this stamp slot.</summary>
    protected override void OnStopHighlight()
    {
        base.OnStopHighlight();
        inkLabel?.Hide();
    }

    private void OnSlotInteractableChanged(bool oldValue, bool newValue) => ApplySlotInteractable(newValue);

    private void OnIsPlacedChanged(bool oldValue, bool newValue) => stampPlaceObjectSlot.IsPlaced = newValue;

    private void ApplySlotInteractable(bool value)
    {
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = value;
    }

    private void OnSpawnedStampRefChanged(NetworkObjectReference previous, NetworkObjectReference current)
    {
        TryResolveSpawnedStamp(current);
    }

    private void TryResolveSpawnedStamp(NetworkObjectReference stampRef)
    {
        if (!stampRef.TryGet(out NetworkObject stampNetObj)) return;
        PickableObject stamp = stampNetObj.GetComponent<PickableObject>();
        if (stamp == null) return;

        spawnedInkStamp = stamp;
        // CanPickUpManually must be false so the player cannot grab the pickup directly —
        // only InkStamp.Interact (slot interaction) sets it to true before a pickup.
        // Collider state is authoritative via _networkInteractableOverride (set by
        // LockInteractableNetworked in SpawnInkStamp), so no local SetInteractable call needed.
        stamp.CanPickUpManually = false;
    }

    private void SpawnInkStamp()
    {
        NetworkObject inkStampNetObj = inkStampPickup.GetComponent<NetworkObject>();
        if (inkStampNetObj == null)
        {
            Debug.LogError("InkStamp: inkStampPickup prefab is missing a NetworkObject component.");
            return;
        }

        NetworkObject inkStamp = Instantiate(
            inkStampNetObj,
            stampPlaceObjectSlot.PlaceObjectPos.position,
            stampPlaceObjectSlot.PlaceObjectPos.rotation
        );
        inkStamp.Spawn();

        spawnedInkStamp = inkStamp.GetComponent<PickableObject>();
        spawnedInkStamp.CanPickUpManually = false;
        // LockInteractableNetworked writes _networkInteractableOverride = 0 as a NetworkVariable
        // so every client receives the locked state reliably via OnValueChanged, regardless of
        // OnNetworkSpawn ordering. It also sets _interactableLocked = true on all clients,
        // which blocks OnHoldingClientChanged from re-enabling colliders on stamp release.
        spawnedInkStamp.LockInteractableNetworked();

        // Publishing to the NetworkVariable replicates the reference to all clients,
        // triggering OnSpawnedStampRefChanged which resolves spawnedInkStamp there too.
        _spawnedStampRef.Value = inkStamp;
    }

    /// <summary>
    /// Enables or disables interaction with this stamp slot on all clients.
    /// Routes through the server so the state persists for late-joiners via the NetworkVariable.
    /// </summary>
    public void SetSlotInteractable(bool value)
    {
        if (IsServer)
            _slotInteractable.Value = value;
        else
            SetSlotInteractableServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetSlotInteractableServerRpc(bool value) => _slotInteractable.Value = value;

    /// <summary>
    /// Permanently disables interaction with both this slot and the spawned stamp pickup on all clients.
    /// Call this once the player has returned the stamp and it should remain locked for the session.
    /// </summary>
    public void LockStampAndSlot()
    {
        SetSlotInteractable(false);
        if (spawnedInkStamp != null)
            spawnedInkStamp.LockInteractableNetworked();
    }

    /// <summary>True while the stamp is sitting in its slot; false once the player has picked it up.</summary>
    public bool IsStampInSlot => _isPlaced.Value;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (spawnedInkStamp == null)
        {
            Debug.LogWarning("InkStamp.Interact: spawnedInkStamp is not yet resolved on this client.");
            return;
        }

        // Guard against a race where two clients both see IsPlaced=true before the
        // server's NetworkVariable write propagates. Only the server mutates _isPlaced.
        if (player.pickupController.HeldObject == null && _isPlaced.Value)
        {
            if (IsServer)
                SetIsPlaced(false);
            else
                SetIsPlacedServerRpc(false);

            // Re-enable the pickup so PickUpObject can claim it; it will immediately disable
            // interactability again as its optimistic lock before the ownership RPC lands.
            spawnedInkStamp.CanPickUpManually = true;
            spawnedInkStamp.SetInteractable(true);

            player.pickupController.PickUpObject(spawnedInkStamp);
        }
    }

    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        if (stampPlaceObjectSlot == null) return;
        if (item.ItemData != stampPlaceObjectSlot.itemThatCanBePlaced.ItemData) return;

        base.InteractWithItem(player, item);
        player.pickupController.DropObject(stampPlaceObjectSlot.PlaceObjectPos);

        // Re-lock the pickup immediately after it is returned to the slot.
        // InkStamp.Interact enabled colliders and set CanPickUpManually = true locally;
        // we must undo that here because LockStampAndSlot's LockInteractableNetworked call
        // will not fire OnValueChanged (the NV is already 0), so the colliders would otherwise
        // remain enabled on the machine that last interacted with the stamp.
        if (spawnedInkStamp != null)
        {
            spawnedInkStamp.CanPickUpManually = false;
            spawnedInkStamp.SetInteractable(false);
        }

        if (IsServer)
            SetIsPlaced(true);
        else
            SetIsPlacedServerRpc(true);
    }

    private void SetIsPlaced(bool value)
    {
        _isPlaced.Value = value;
        stampPlaceObjectSlot.IsPlaced = value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetIsPlacedServerRpc(bool value) => SetIsPlaced(value);
}
