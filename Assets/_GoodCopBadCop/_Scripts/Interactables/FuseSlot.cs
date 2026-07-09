using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single labelled slot inside the fuse-box panel that accepts one specific-colored
/// <see cref="FusePickup"/>.
///
/// Implements <see cref="IHeldItemPassthrough"/> so <see cref="PlayerInteractionController"/>
/// routes the LMB event to <see cref="Interact"/> even while the player is holding an item
/// — bypassing the normal <c>itemsThatCanInteractWith</c> array check.
///
/// When a player aims at this slot while holding a <see cref="FusePickup"/> whose
/// <see cref="FusePickup.FuseColor"/> matches <see cref="_expectedColor"/> and presses LMB:
///   1. The fuse is consumed via <see cref="PlayerPickupController.DestroyEquippedItem"/>.
///   2. A server RPC marks this slot as filled.
///   3. The <see cref="_emptyVisual"/> is hidden and <see cref="_filledVisual"/> is shown
///      on every client via the <see cref="_isFilled"/> NetworkVariable.
///   4. <see cref="OnFuseInserted"/> fires on every client for <see cref="FuseBoxPuzzleController"/>
///      to detect puzzle completion.
///
/// Setup notes:
///   - Attach to a child of the Fuse Box prefab; one per slot (3 total).
///   - Set <see cref="_expectedColor"/> to the slot's designated fuse color.
///   - Assign the empty-slot and filled-slot child GameObjects to the visual fields.
///   - Optionally assign a short insert sound clip.
///   - Set <see cref="interactText"/> on the base Interactable to e.g. "Insert Red Fuse".
/// </summary>
[RequireComponent(typeof(Collider))]
public class FuseSlot : Interactable, IHeldItemPassthrough
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Fuse Slot")]
    [Tooltip("The color of fuse this slot accepts.")]
    [SerializeField] private FuseColor _expectedColor;

    [Tooltip("Visual shown when the slot is empty and waiting for a fuse.")]
    [SerializeField] private GameObject _emptyVisual;

    [Tooltip("Visual shown after a fuse has been successfully inserted.")]
    [SerializeField] private GameObject _filledVisual;

    [Tooltip("Sound played on all clients when a fuse is successfully inserted.")]
    [SerializeField] private AudioClip _insertSound;

    // ── Networked state ───────────────────────────────────────────────────────

    private readonly NetworkVariable<bool> _isFilled = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True once a matching fuse has been inserted into this slot.</summary>
    public bool IsFilled => _isFilled.Value;

    /// <summary>
    /// Fired on all clients (driven by the <see cref="_isFilled"/> NetworkVariable)
    /// when this slot receives its fuse. <see cref="FuseBoxPuzzleController"/> subscribes
    /// to this event to detect when all slots are filled.
    /// </summary>
    public event Action OnFuseInserted;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isFilled.OnValueChanged += OnFilledChanged;
        ApplyFilledState(_isFilled.Value);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isFilled.OnValueChanged -= OnFilledChanged;
    }

    private void OnFilledChanged(bool previous, bool current) => ApplyFilledState(current);

    private void ApplyFilledState(bool filled)
    {
        if (_emptyVisual  != null) _emptyVisual.SetActive(!filled);
        if (_filledVisual != null) _filledVisual.SetActive(filled);

        // Disable the collider once filled so the reticle no longer targets this slot.
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = !filled;

        if (filled)
        {
            OnFuseInserted?.Invoke();
        }
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerInteractionController.TryItemUse"/> via the
    /// <see cref="IHeldItemPassthrough"/> path when the local player presses LMB
    /// while holding any item and aiming at this slot.
    ///
    /// Validates that the held item is a <see cref="FusePickup"/> with the
    /// matching color, then consumes it and sends an RPC to the server to mark
    /// the slot as filled.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (_isFilled.Value) return;

        FusePickup fuse = player.pickupController.HeldObject as FusePickup;
        if (fuse == null)
        {
            Debug.Log($"[FuseSlot:{_expectedColor}] Interact: held item is not a FusePickup.");
            return;
        }

        if (fuse.FuseColor != _expectedColor)
        {
            Debug.Log($"[FuseSlot:{_expectedColor}] Interact: wrong color — held {fuse.FuseColor}.");
            return;
        }

        // Consume the fuse from the player's hand and destroy it. This handles all
        // local state cleanup (animations, containers, NetworkVariable itemEquippedIndex)
        // and sends DespawnServerRpc to the server.
        player.pickupController.DestroyEquippedItem();

        // Notify the server to mark this slot as filled.
        MarkFilledServerRpc();

        Debug.Log($"[FuseSlot:{_expectedColor}] Fuse inserted by local client.");
    }

    // ── Server RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Received on the server: marks this slot as filled, which broadcasts the state
    /// change to all clients via the <see cref="_isFilled"/> NetworkVariable.
    /// Guards against a race where two clients insert simultaneously.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void MarkFilledServerRpc()
    {
        if (_isFilled.Value) return;
        _isFilled.Value = true;
        PlayInsertSoundClientRpc();
        Debug.Log($"[FuseSlot:{_expectedColor}] Server: slot marked filled.");
    }

    // ── Client RPC ────────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayInsertSoundClientRpc()
    {
        if (_insertSound != null)
            SFXController.Instance?.PlayAtPosition(_insertSound, transform.position);
    }

    // ── Highlight override ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the interact hint text based on whether the player is holding the
    /// correct fuse. The base reticle still shows a button icon when ShowInteractHint
    /// is true and the interactText is set.
    /// </summary>
    public override bool ShowInteractHint => !_isFilled.Value;
}
