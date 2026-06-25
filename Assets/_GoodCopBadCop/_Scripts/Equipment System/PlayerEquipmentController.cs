using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages wearable equipment slots for the player character.
/// Tracks worn items via NetworkVariables so all clients reflect the correct visual state.
/// The local owner sees first-person overlays; the mask mesh is only shown on observed
/// (non-owner) clients since they see the character from the outside.
/// </summary>
public class PlayerEquipmentController : NetworkBehaviour
{
    [Tooltip("SkinnedMeshRenderer for the radiation mask on the observed character model.")]
    [SerializeField] private SkinnedMeshRenderer radiationMaskRenderer;

    [Tooltip("Item data for the Radiation Mask — used to spawn the pickup when unequipping.")]
    [SerializeField] private PickableItemData radiationMaskItemData;

    [Tooltip("Multiplier applied to all radiation accumulation while the mask is worn. 0.2 = 20% of normal rate.")]
    [SerializeField] private float maskRadiationMultiplier = 0.2f;

    private PlayerPickupController _pickupController;
    private PlayerRadiation _radiationController;

    private readonly NetworkVariable<bool> _isMaskEquipped = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Returns true if this player currently has the radiation mask equipped.</summary>
    public bool IsMaskEquipped => _isMaskEquipped.Value;

    private void Awake()
    {
        _pickupController = GetComponent<PlayerPickupController>();
        _radiationController = GetComponent<PlayerRadiation>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isMaskEquipped.OnValueChanged += OnMaskEquippedChanged;
        // Apply initial state for late-joining clients
        UpdateMaskVisibility(_isMaskEquipped.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isMaskEquipped.OnValueChanged -= OnMaskEquippedChanged;
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (!_isMaskEquipped.Value) return;
        if (Input.GetKeyDown(KeyCode.V))
            UnequipMask();
    }

    private void OnMaskEquippedChanged(bool previous, bool current) => UpdateMaskVisibility(current);

    private void UpdateMaskVisibility(bool equipped)
    {
        if (radiationMaskRenderer != null)
        {
            // Only show the mask mesh on observed (non-owner) clients.
            radiationMaskRenderer.enabled = equipped && !IsOwner;
        }

        // Drive the local first-person overlay for the owner only.
        if (IsOwner && MaskOverlayController.Instance != null)
            MaskOverlayController.Instance.SetVisible(equipped);

        // Radiation runs server-side only — set the multiplier only on the server.
        if (IsServer && _radiationController != null)
            _radiationController.RadiationMultiplier = equipped ? maskRadiationMultiplier : 1f;
    }

    /// <summary>
    /// Equips the radiation mask for this player. Only the owner may call this.
    /// Broadcasts the state change to all clients via the server.
    /// </summary>
    public void EquipMask()
    {
        if (!IsOwner)
        {
            Debug.LogWarning("[PlayerEquipmentController] EquipMask called on a non-owner client. Ignoring.");
            return;
        }

        EquipMaskServerRpc();
    }

    /// <summary>
    /// Unequips the radiation mask and spawns it as a holdable pickup in the player's hand.
    /// Only the owner may call this, and only when hands are empty.
    /// Requires <see cref="radiationMaskItemData"/> to be assigned and registered in the ItemDatabase.
    /// </summary>
    public void UnequipMask()
    {
        if (!IsOwner) return;
        if (!_isMaskEquipped.Value) return;

        // Require empty hands so the spawned pickup can be immediately picked up.
        if (_pickupController != null && _pickupController.IsHoldingObject)
        {
            Debug.Log("[PlayerEquipmentController] Cannot unequip mask while holding another object.");
            return;
        }

        if (radiationMaskItemData == null)
        {
            Debug.LogError("[PlayerEquipmentController] radiationMaskItemData is not assigned — assign Radiation Mask.asset in the Inspector.");
            return;
        }

        UnequipMaskServerRpc();

        // Spawn the mask pickup at the hold point so it lands directly in the player's hand.
        Transform spawnPoint = _pickupController != null ? _pickupController.holdPoint : transform;
        _pickupController?.SpawnAndPickUp(radiationMaskItemData, spawnPoint);
    }

    [ServerRpc]
    private void EquipMaskServerRpc() => _isMaskEquipped.Value = true;

    [ServerRpc]
    private void UnequipMaskServerRpc() => _isMaskEquipped.Value = false;
}
