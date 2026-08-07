using GoodCopBadCop.Input;
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

    [Tooltip("Sound effect played (at the player's position, for everyone nearby) when the radiation mask is equipped.")]
    [SerializeField] private AudioClip maskEquipSound;

    [Header("Backpack")]
    [Tooltip("The backpack mesh GameObject on the player's back. Shown when a BackpackPickable is equipped.")]
    [SerializeField] private GameObject backpackMesh;

    [Tooltip("Transform on the player's back that the equipped BackpackPickable world object follows.")]
    [SerializeField] private Transform backpackAnchor;

    /// <summary>Anchor on the player's back for the BackpackPickable world object constraint.</summary>
    public Transform BackpackAnchor => backpackAnchor;

    /// <summary>Shows or hides the backpack mesh on this player's character model.</summary>
    public void ShowBackpackMesh(bool visible)
    {
        if (backpackMesh != null)
            backpackMesh.SetActive(visible);
    }

    [Tooltip("Multiplier applied to all radiation accumulation while the mask is worn. 0.05 = 5% of normal rate (much less radiation while masked).")]
    [SerializeField] private float maskRadiationMultiplier = 0.05f;

    private PlayerPickupController _pickupController;
    private PlayerRadiation _radiationController;
    private PlayerHatController _hatController;

    /// <summary>Hat index that was equipped before the mask was worn, so it can be restored on unequip. -1 = none.</summary>
    private int _hatIndexBeforeMask = -1;

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
        _hatController = GetComponent<PlayerHatController>();
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
        if (RebindableInput.GetKeyDown(GameAction.ToggleMask))
            UnequipMask();
    }

    private void OnMaskEquippedChanged(bool previous, bool current)
    {
        UpdateMaskVisibility(current);

        // Play the equip sound exactly once, on the equip transition — every client hears it
        // spatially at the wearer's position. UpdateMaskVisibility alone can't distinguish a real
        // transition from the initial state sync applied to late-joiners in OnNetworkSpawn.
        if (current && maskEquipSound != null && SFXController.Instance != null)
            SFXController.Instance.PlayAtPosition(maskEquipSound, transform.position);
    }

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

        // Drive the helper icon in the player HUD for the owner only.
        if (IsOwner && PlayerUI.Instance != null)
            PlayerUI.Instance.SetMaskHelperIconVisible(equipped);

        // RadiationMultiplier is a plain (non-networked) field read locally by every machine's
        // own PlayerRadiation instance — e.g. RadiationHotspot/OffTrailRadiation run unguarded on
        // every client and read whichever local copy they find. _isMaskEquipped is already
        // network-synced (NetworkVariable), so every machine — server and every client — should
        // mirror it into its own local RadiationMultiplier to keep local feedback (UI, tick audio)
        // consistent with the authoritative mask state. Restricting this to IsServer left non-host
        // clients always computing with an un-masked (1x) multiplier locally.
        if (_radiationController != null)
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

        // Temporarily hide whatever hat is currently worn — it reappears when the mask is unequipped.
        if (_hatController != null && _hatController.EquippedHatIndex != -1)
        {
            _hatIndexBeforeMask = _hatController.EquippedHatIndex;
            _hatController.RemoveHat();
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

        // Restore whatever hat was worn before the mask went on.
        if (_hatController != null && _hatIndexBeforeMask != -1)
        {
            _hatController.EquipHat(_hatIndexBeforeMask);
            _hatIndexBeforeMask = -1;
        }

        // Spawn the mask pickup at the hold point so it lands directly in the player's hand.
        Transform spawnPoint = _pickupController != null ? _pickupController.holdPoint : transform;
        _pickupController?.SpawnAndPickUp(radiationMaskItemData, spawnPoint);
    }

    [ServerRpc]
    private void EquipMaskServerRpc() => _isMaskEquipped.Value = true;

    [ServerRpc]
    private void UnequipMaskServerRpc() => _isMaskEquipped.Value = false;
}
