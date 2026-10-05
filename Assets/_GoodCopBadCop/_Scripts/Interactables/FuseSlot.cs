using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A slot inside the fuse-box panel that accepts any <see cref="FusePickup"/>.
///
/// Interaction rules:
///   – Empty slot + player holding a FusePickup  → LMB / RT inserts the fuse (snaps it to the slot).
///   – Filled slot + empty-handed player          → Interact (E) extracts the fuse (player picks it up).
///
/// The fuse is kept as a live NetworkObject: it is parent-constrained to this slot on all
/// clients (via <see cref="PickableObject.PlaceInSlotServerRpc"/>) and locked so it cannot be
/// grabbed directly from the world while seated. Extracting reverses this in full.
///
/// Setup notes:
///   - This must be a child of the Fuse Box, which requires a NetworkObject component.
///   - Assign <see cref="_emptyVisual"/> / <see cref="_filledVisual"/> as needed.
///     The live FusePickup mesh acts as the primary in-slot visual; _filledVisual is optional.
///   - Assign insert / extract audio clips for feedback.
///   - Set <see cref="Interactable.interactText"/> in the Inspector (e.g. "Insert Fuse").
/// </summary>
[RequireComponent(typeof(Collider))]
public class FuseSlot : Interactable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Fuse Slot")]
    [Tooltip("Visual shown when the slot is empty and waiting for a fuse.")]
    [SerializeField] private GameObject _emptyVisual;

    [Tooltip("Optional static visual shown when a fuse is seated. The live FusePickup mesh is also present.")]
    [SerializeField] private GameObject _filledVisual;

    [Tooltip("Sound played on all clients when a fuse is inserted.")]
    [SerializeField] private AudioClip _insertSound;

    [Tooltip("Sound played on all clients when a fuse is extracted.")]
    [SerializeField] private AudioClip _extractSound;

    // ── Networked state ───────────────────────────────────────────────────────

    /// <summary>
    /// Reference to the fuse currently seated in this slot.
    /// NetworkObjectId == 0 means the slot is empty (default struct value).
    /// </summary>
    private readonly NetworkVariable<NetworkObjectReference> _insertedFuse = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True while a fuse is seated in this slot.</summary>
    public bool IsFilled => _insertedFuse.Value.NetworkObjectId != 0;

    /// <summary>Fired on all clients the moment a fuse is successfully inserted.</summary>
    public event Action OnFuseInserted;

    /// <summary>Fired on all clients the moment a fuse is extracted from this slot.</summary>
    public event Action OnFuseExtracted;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _insertedFuse.OnValueChanged += OnInsertedFuseChanged;
        // Sync visuals for late-joining clients.
        ApplySlotState(IsFilled);
        SetFuseSeated(_insertedFuse.Value, true);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _insertedFuse.OnValueChanged -= OnInsertedFuseChanged;
    }

    private void OnInsertedFuseChanged(NetworkObjectReference prev, NetworkObjectReference current)
    {
        bool wasFilled = prev.NetworkObjectId != 0;
        bool nowFilled = current.NetworkObjectId != 0;

        ApplySlotState(nowFilled);

        // Seated fuses stop glowing; an extracted fuse resumes the "find the fuses" glow if active.
        SetFuseSeated(prev, false);
        SetFuseSeated(current, true);

        if (!wasFilled && nowFilled)
            OnFuseInserted?.Invoke();
        else if (wasFilled && !nowFilled)
            OnFuseExtracted?.Invoke();
    }

    private static void SetFuseSeated(NetworkObjectReference fuseRef, bool seated)
    {
        if (fuseRef.NetworkObjectId == 0) return;
        if (fuseRef.TryGet(out NetworkObject netObj) && netObj.TryGetComponent(out FusePickup fuse))
            fuse.SetSeated(seated);
    }

    private void ApplySlotState(bool filled)
    {
        if (_emptyVisual  != null) _emptyVisual.SetActive(!filled);
        if (_filledVisual != null) _filledVisual.SetActive(filled);
        // Collider stays enabled in both states — filled slot is interactable for extraction.
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>Fuse slots don't show the reticle's action-text hint on hover.</summary>
    public override bool ShowInteractHint => false;

    /// <summary>
    /// No Interact key icon — the only prompt is the LMB / RT use icon, shown while holding a
    /// fuse over an empty slot (driven by <see cref="CanInteractWithItem"/>).
    /// </summary>
    public override bool ShowsInteractPrompt(PlayerInteractionController player) => false;

    /// <summary>An empty slot accepts any held <see cref="FusePickup"/> (LMB / RT).</summary>
    public override bool CanInteractWithItem(PickableObject item)
        => !IsFilled && item is FusePickup;

    public override string GetItemUseVerb(PlayerInteractionController player, PickableObject item) => "Insert fuse";

    /// <summary>Interact key (empty-handed) on a filled slot extracts the fuse.</summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsFilled && !player.pickupController.IsHoldingObject)
            ExtractFuse(player);
    }

    /// <summary>LMB / RT while holding a fuse over an empty slot inserts it.</summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        base.InteractWithItem(player, item);

        if (IsFilled) return;
        FusePickup fuse = item as FusePickup;
        if (fuse == null || player.pickupController.HeldObject != fuse) return;

        // Use the existing PlaceInSlot infrastructure: snaps the fuse to this slot's
        // world transform on all clients and parents it via ParentConstraint.
        // Requires the Fuse Box (ancestor) to have a NetworkObject component.
        player.pickupController.DropObject(transform);

        // Notify the server to record the fuse reference and prevent direct pickup.
        InsertFuseServerRpc(new NetworkObjectReference(fuse.NetworkObject));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Picks the seated fuse back up into the player's hand and schedules a server-side
    /// slot clear. Calls <see cref="PlayerPickupController.PickUpObject"/> directly to
    /// bypass the <see cref="PickableObject.Interact"/> interactability guard (the fuse
    /// collider is intentionally disabled while seated).
    /// </summary>
    private void ExtractFuse(PlayerInteractionController player)
    {
        if (!_insertedFuse.Value.TryGet(out NetworkObject fuseNetObj))
        {
            Debug.LogWarning($"[FuseSlot] ExtractFuse: could not resolve fuse NetworkObject on client {NetworkManager.Singleton.LocalClientId}.");
            return;
        }

        if (!fuseNetObj.TryGetComponent<FusePickup>(out FusePickup fuse))
        {
            Debug.LogWarning($"[FuseSlot] ExtractFuse: seated object is not a FusePickup.");
            return;
        }

        // Pick up the fuse directly — PickUpObject handles constraint swap, ownership
        // transfer, and arm animation. No need to re-enable the collider first because
        // PickUpObject does not check IsInteractable() itself.
        player.pickupController.PickUpObject(fuse);

        // Tell the server to clear the slot and restore the fuse's normal interactability.
        ClearSlotServerRpc();
    }

    // ── Server RPCs ───────────────────────────────────────────────────────────

    /// <summary>
    /// Received on the server: records the inserted fuse and locks it from direct pickup.
    /// Guards against simultaneous inserts from two clients.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void InsertFuseServerRpc(NetworkObjectReference fuseRef)
    {
        if (IsFilled) return;

        if (!fuseRef.TryGet(out NetworkObject fuseNetObj))
        {
            Debug.LogWarning("[FuseSlot] InsertFuseServerRpc: could not resolve fuse ref on server.");
            return;
        }

        // Prevent the fuse from being grabbed directly while it is seated in the slot.
        // LockInteractableNetworked sets _networkInteractableOverride = 0 on all clients,
        // overriding the holder-based enable that fires after ReleaseHolderServerRpc.
        if (fuseNetObj.TryGetComponent<FusePickup>(out var fuse))
            fuse.LockInteractableNetworked();

        _insertedFuse.Value = fuseRef;

        PlayInsertSoundClientRpc();
        Debug.Log($"[FuseSlot] Fuse '{fuseNetObj.name}' inserted into slot '{name}'.");
    }

    /// <summary>
    /// Received on the server: unlocks the fuse and clears the slot reference.
    /// Guards against an empty-slot clear (e.g. two players extract simultaneously).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ClearSlotServerRpc()
    {
        if (!IsFilled) return;

        // Restore holder-based interactability on the fuse. After PickUpObject sends
        // RequestOwnershipServerRpc, _holdingClientId will reflect the new owner,
        // so ApplyNetworkInteractableState will correctly leave the collider disabled
        // (object is being held) once _networkInteractableOverride returns to -1.
        if (_insertedFuse.Value.TryGet(out NetworkObject fuseNetObj))
        {
            if (fuseNetObj.TryGetComponent<FusePickup>(out var fuse))
                fuse.UnlockInteractableNetworked();
        }

        _insertedFuse.Value = default;

        PlayExtractSoundClientRpc();
        Debug.Log($"[FuseSlot] Slot '{name}' cleared.");
    }

    // ── Client RPCs ───────────────────────────────────────────────────────────

    [ClientRpc]
    private void PlayInsertSoundClientRpc()
    {
        if (_insertSound != null)
            SFXController.Instance?.PlayAtPosition(_insertSound, transform.position);
    }

    [ClientRpc]
    private void PlayExtractSoundClientRpc()
    {
        if (_extractSound != null)
            SFXController.Instance?.PlayAtPosition(_extractSound, transform.position);
    }
}
