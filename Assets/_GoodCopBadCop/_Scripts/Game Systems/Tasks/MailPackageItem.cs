using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using TMPro;

/// <summary>
/// The bin a <see cref="MailPackageItem"/> should be dropped into.
/// </summary>
public enum MailSortBinType
{
    Delivery,
    Quarantine,
    Confiscate
}

/// <summary>
/// A single piece of mail spawned by <see cref="SortMailTask"/>. Carries the addressee's name,
/// a goods-category label, and the bin it must be dropped into to be sorted correctly.
///
/// Extends <see cref="PickableObject"/> so it can be picked up, carried, and physically dropped
/// by the player like any other pickable prop. Sorting itself is detected by <see cref="MailSortBin"/>
/// via a trigger collider on each bin — this component does not need to be interacted with directly.
///
/// Prefab requirements (in addition to the standard PickableObject setup — NetworkObject, Rigidbody,
/// NetworkRigidbody, ParentConstraint, PickableColliderController, HighlightEffect, Interactable-layer
/// collider):
///   - Optionally assign <see cref="_residentNameText"/> / <see cref="_goodsLabelText"/> to child
///     TextMeshPro components (world-space label on the package) to show the addressee and goods
///     type on the box. Both are optional — if unassigned, only <see cref="interactText"/> is set.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MailPackageItem : PickableObject
{
    [Header("Mail Label (optional)")]
    [Tooltip("World-space label showing the resident's name. Optional.")]
    [SerializeField] private TextMeshPro _residentNameText;

    [Tooltip("World-space label showing the goods category. Optional.")]
    [SerializeField] private TextMeshPro _goodsLabelText;

    [Header("Sort Feedback")]
    [Tooltip("One-shot sound played on all clients whenever this package is correctly sorted (into Confiscate or into its addressee's mailbox cubby).")]
    [SerializeField] private AudioClip _sortSuccessSfxClip;
    [Tooltip("Volume for _sortSuccessSfxClip.")]
    [SerializeField] private float _sortSuccessSfxVolume = 1f;

    [Header("Addressee (set by SortMailTask.ServerInitialize at spawn time)")]
    [Tooltip("Inspector-only debug display of the resident this package is addressed to — read-only at runtime; the authoritative value is the replicated _residentPoolIndex below. Not used for sort-matching directly.")]
    [SerializeField] private SuspectData _assignedResidentDebugView;

    /// <summary>
    /// The resident this package is actually addressed to, resolved from the replicated
    /// <see cref="_residentPoolIndex"/> via <see cref="MailCubbyManager.ResolveResident"/> — this
    /// resolves correctly on every client (not just the server), because the index itself is a
    /// networked value. Compared directly (by reference) against
    /// <see cref="MailCubbySlot.AssignedResident"/>, both server-side in
    /// <see cref="SortMailTask.EvaluateSort"/> and client-side (optimistically) in
    /// <see cref="MailCubbySlot.HandleItemPlaced"/>.
    ///
    /// IMPORTANT: this must be resolved from a *networked* value, not a plain
    /// server-only field — a plain field reads back null/default on every client that isn't the
    /// server, silently breaking <see cref="MailCubbySlot.HandleItemPlaced"/>'s optimistic
    /// <see cref="LockInteractable"/> call (since its correctness check would always be false)
    /// and reopening the "grab it back out before the server lock lands" race for exactly the
    /// clients that need it most. That was the cause of the intermittent "random box doesn't
    /// register" regression.
    /// </summary>
    public SuspectData AssignedResident => MailCubbyManager.Instance != null
        ? MailCubbyManager.Instance.ResolveResident(_residentPoolIndex.Value)
        : null;

    // ── Networked data, set once by the server at spawn time via ServerInitialize ──────────────

    /// <summary>Index of <see cref="AssignedResident"/> within <see cref="MailCubbyManager"/>'s shared resident pool. Replicated so every client (not just the server) can resolve the real SuspectData reference — see <see cref="AssignedResident"/>.</summary>
    private readonly NetworkVariable<int> _residentPoolIndex = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> _residentName = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> _goodsLabel = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _correctBin = new(
        (int)MailSortBinType.Delivery,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Controls the persistent findability glow for this unresolved delivery package. Networked so
    /// every client — including late joiners — sees the same active-package call-out.
    /// </summary>
    private readonly NetworkVariable<bool> _deliveryHighlightActive = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// True while this package rides inside the delivery truck's crate (see
    /// <see cref="SortMailTask.TryPrepareCrateDelivery"/>). Pinned packages follow the crate via
    /// <see cref="SocketFollow"/> on every peer with NetworkTransform disabled, kinematic, colliders
    /// off, locked and un-highlighted — mirroring supply-box containment. Replicated (with the
    /// crate-local pose below) so late joiners rebuild the pin on spawn.
    /// </summary>
    private readonly NetworkVariable<bool> _pinnedToCrate = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Pinned pose in the crate transform's local space (already scale-compensated).</summary>
    private readonly NetworkVariable<Vector3> _crateLocalPosition = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<Quaternion> _crateLocalRotation = new(
        Quaternion.identity,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool IsPinnedToCrate => _pinnedToCrate.Value;

    /// <summary>
    /// Replicated "correctly sorted" flag (set in <see cref="ResolveAndSettle"/>). Gates
    /// <see cref="IsInteractable"/> on every peer, including late joiners. Collider-based locking
    /// alone is not enough: <see cref="PickableColliderController.SetReleased"/> (from
    /// <see cref="PickableObject.OnDropped"/> and the held→released <c>_holdingClientId</c>
    /// change) re-enables the root physics colliders right after the lock is applied, and
    /// <see cref="PickableObject.Interact"/> treats any enabled root collider as pickable.
    /// </summary>
    private readonly NetworkVariable<bool> _isSorted = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Local optimistic sorted flag set by <see cref="LockAsSorted"/> before the server confirms.</summary>
    private bool _sortedLocally;

    /// <summary>True once this package has been sorted (server-confirmed or optimistically on this peer). Sorted packages can never be picked up again.</summary>
    public bool IsSorted => _isSorted.Value || _sortedLocally;

    public override bool IsInteractable => base.IsInteractable && !IsSorted;

    /// <summary>True once this package has been correctly sorted and is pending despawn. Server-only guard against double-counting.</summary>
    public bool IsResolved { get; private set; }

    /// <summary>
    /// Local, optimistic: immediately makes this package non-interactable on this peer the moment
    /// it is placed in its addressee's cubby, before the server's <see cref="MarkDelivered"/>
    /// confirmation replicates. See <see cref="MailCubbySlot.HandleItemPlaced"/>.
    /// </summary>
    public void LockAsSorted()
    {
        _sortedLocally = true;
        LockInteractable();
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (IsSorted) return;
        base.Interact(player);
    }

    public string ResidentName => _residentName.Value.ToString();
    public string GoodsLabel   => _goodsLabel.Value.ToString();
    public MailSortBinType CorrectBin => (MailSortBinType)_correctBin.Value;
    public int ResidentPoolIndex => _residentPoolIndex.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _residentName.OnValueChanged += (_, _) => RefreshLabel();
        _goodsLabel.OnValueChanged   += (_, _) => RefreshLabel();
        _deliveryHighlightActive.OnValueChanged += OnDeliveryHighlightChanged;
        _pinnedToCrate.OnValueChanged += OnPinnedToCrateChanged;
        RefreshLabel();
        ApplyDeliveryHighlight(_deliveryHighlightActive.Value);

        if (_pinnedToCrate.Value)
            ApplyCratePinLocal();
    }

    public override void OnNetworkDespawn()
    {
        _deliveryHighlightActive.OnValueChanged -= OnDeliveryHighlightChanged;
        _pinnedToCrate.OnValueChanged -= OnPinnedToCrateChanged;
        ApplyDeliveryHighlight(false);
        base.OnNetworkDespawn();
    }

    private void OnDeliveryHighlightChanged(bool previous, bool current) => ApplyDeliveryHighlight(current);

    private void ApplyDeliveryHighlight(bool highlight) =>
        SetForceHighlight(highlight, HighlightHold.MailDelivery);

    /// <summary>
    /// Server-only. Assigns this package's addressee, goods category, and correct sorting bin.
    /// Call immediately after spawning, before the object is observed by clients if possible.
    /// </summary>
    /// <param name="resident">
    /// The addressee's <see cref="SuspectData"/> asset. Its index within
    /// <see cref="MailCubbyManager"/>'s shared resident pool is stored as the replicated
    /// <see cref="_residentPoolIndex"/> and resolved back to the real reference via
    /// <see cref="AssignedResident"/> on every client — see that property's doc comment for why
    /// this must go through a networked value rather than a plain field.
    /// </param>
    public void ServerInitialize(SuspectData resident, string residentName, string goodsLabel, MailSortBinType correctBin, bool highlight = true)
    {
        if (!IsServer) return;

        _assignedResidentDebugView = resident;
        _residentPoolIndex.Value   = MailCubbyManager.Instance != null ? MailCubbyManager.Instance.GetResidentIndex(resident) : -1;
        _residentName.Value        = residentName;
        _goodsLabel.Value          = goodsLabel;
        _correctBin.Value          = (int)correctBin;
        _deliveryHighlightActive.Value = highlight;
        ApplyDeliveryHighlight(highlight);
        IsResolved                 = false;
        _isSorted.Value            = false;

        RefreshLabel();
    }

    // ── Crate pinning (delivery truck ride-along) ─────────────────────────────

    /// <summary>
    /// Server-only, call BEFORE Spawn. Pins this package inside the delivery crate at the given
    /// crate-local pose. The values ship with the spawn payload, so every peer (including late
    /// joiners) applies the pin in <see cref="OnNetworkSpawn"/> — same pattern as
    /// <see cref="PickableObject.SetSupplyBoxContainedNetworked(NetworkObjectReference, string)"/>.
    /// </summary>
    public void SetCratePinBeforeSpawn(Vector3 crateLocalPosition, Quaternion crateLocalRotation)
    {
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer)
        {
            Debug.LogWarning($"[MailPackageItem] Only the server can pin {name} to the delivery crate.", this);
            return;
        }

        _crateLocalPosition.Value = crateLocalPosition;
        _crateLocalRotation.Value = crateLocalRotation;
        _pinnedToCrate.Value      = true;
    }

    /// <summary>
    /// Server-only, call right after Spawn. NetworkRigidbody can restore the prefab's
    /// non-kinematic state during spawn (after our OnNetworkSpawn), so re-assert the pin.
    /// </summary>
    public void EnsureCratePinAppliedOnServer()
    {
        if (!IsServer || !_pinnedToCrate.Value) return;
        ApplyCratePinLocal();
    }

    /// <summary>
    /// Server-only. Un-pins this package from the crate where it currently sits, enables
    /// server-authoritative physics (it settles onto the crate floor), restores solid colliders,
    /// unlocks interaction and turns on the delivery highlight. Clients snap to the server pose
    /// and hand position over to NetworkTransform.
    /// </summary>
    public void ReleaseFromCrate()
    {
        if (!IsServer || !_pinnedToCrate.Value) return;

        Vector3    position = transform.position;
        Quaternion rotation = transform.rotation;

        // Use the crate's current (landed) pose directly rather than the last SocketFollow frame.
        Transform crate = SortMailTask.Instance != null ? SortMailTask.Instance.DeliveryCrateTransform : null;
        if (crate != null)
        {
            position = crate.TransformPoint(_crateLocalPosition.Value);
            rotation = crate.rotation * _crateLocalRotation.Value;
        }

        _pinnedToCrate.Value = false;
        ReleaseCratePinLocal(position, rotation);
        ReleaseFromCrateClientRpc(position, rotation);

        UnlockInteractableNetworked();
        _deliveryHighlightActive.Value = true;
        ApplyDeliveryHighlight(true);
    }

    [ClientRpc]
    private void ReleaseFromCrateClientRpc(Vector3 position, Quaternion rotation)
    {
        if (IsServer) return;
        ReleaseCratePinLocal(position, rotation);
    }

    private void OnPinnedToCrateChanged(bool previous, bool current)
    {
        // Pin is applied from the spawn payload; release normally arrives via the ClientRpc with
        // the authoritative pose. This is just a safety net if the NV delta lands first.
        if (current)
            ApplyCratePinLocal();
        else if (!IsServer)
            ReleaseCratePinLocal(transform.position, transform.rotation);
    }

    /// <summary>Per-peer: follow the delivery crate at the replicated crate-local pose.</summary>
    private void ApplyCratePinLocal()
    {
        Transform crate = SortMailTask.Instance != null ? SortMailTask.Instance.DeliveryCrateTransform : null;
        if (crate == null)
        {
            Debug.LogWarning($"[MailPackageItem] {name} is pinned to the delivery crate, but no crate transform could be resolved.", this);
            return;
        }

        // NetworkTransform would fight the per-peer SocketFollow — every peer drives the pose locally.
        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = false;

        if (_rb != null)
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
        }

        // Also forces kinematic and disables physics colliders (PickableColliderController.SetHeld).
        SetSocketFollowWithLocalOffset(crate, _crateLocalPosition.Value, _crateLocalRotation.Value);
        transform.SetPositionAndRotation(
            crate.TransformPoint(_crateLocalPosition.Value),
            crate.rotation * _crateLocalRotation.Value);
    }

    /// <summary>Per-peer: stop following the crate and hand the package back to physics / NetworkTransform.</summary>
    private void ReleaseCratePinLocal(Vector3 position, Quaternion rotation)
    {
        ClearSocketFollow();
        transform.SetPositionAndRotation(position, rotation);

        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;

        if (_rb != null)
        {
            // Server simulates; clients stay kinematic and follow NetworkTransform.
            _rb.isKinematic = !IsServer;
            if (IsServer)
            {
                _rb.linearVelocity  = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
        }

        PickableColliderController colliderController = GetComponent<PickableColliderController>();
        if (colliderController != null) colliderController.SetReleased();

        Physics.SyncTransforms();
    }

    /// <summary>
    /// Server-only. Marks this package as resolved, permanently locks it so it can no longer be
    /// picked up (via <see cref="LockInteractableNetworked"/>), and plays the sort success sound
    /// effect on every client. Called by <see cref="SortMailTask.EvaluateSort"/> when this
    /// package is dropped into its addressee's mailbox cubby — delivered packages are not
    /// despawned immediately; they stay sitting in the mailbox until
    /// <see cref="SortMailTask.DespawnResolvedPackages"/> clears them at the start of the next
    /// day. See <see cref="ResolveAndSettle"/> for how throw momentum is cleared before locking.
    /// </summary>
    public void MarkDelivered(bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        if (!IsServer) return;
        ResolveAndSettle(hasSnapPose, snapPosition, snapRotation);
    }

    /// <summary>
    /// Server-only. Marks this package as resolved, permanently locks it so it can no longer be
    /// picked up, and plays the sort success sound effect on every client. Called by
    /// <see cref="SortMailTask.EvaluateSort"/> when this package is correctly sorted into a
    /// Confiscate bin — like a correct delivery, the package is not despawned immediately; it
    /// stays sitting in the bin until <see cref="SortMailTask.DespawnResolvedPackages"/> clears it
    /// at the start of the next day. See <see cref="ResolveAndSettle"/> for how throw momentum is
    /// cleared before locking.
    /// </summary>
    public void MarkConfiscated(bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        if (!IsServer) return;
        ResolveAndSettle(hasSnapPose, snapPosition, snapRotation);
    }

    /// <summary>
    /// Server-only. Shared finalization for a correct sort (Confiscate or Delivery): always
    /// zeroes this package's Rigidbody velocity and makes it kinematic in place before disabling
    /// its colliders — otherwise a package that still has throw momentum when its collider is
    /// locked out (via <see cref="LockInteractableNetworked"/>) keeps travelling with nothing left
    /// to stop it and flies straight through the floor. If <paramref name="hasSnapPose"/> is true,
    /// the package is snapped to <paramref name="snapPosition"/>/<paramref name="snapRotation"/>
    /// (e.g. a cubby's <see cref="PlacementSlot"/> pose) rather than just freezing wherever it
    /// currently sits.
    /// </summary>
    private void ResolveAndSettle(bool hasSnapPose, Vector3 snapPosition, Quaternion snapRotation)
    {
        Vector3    position = hasSnapPose ? snapPosition : transform.position;
        Quaternion rotation = hasSnapPose ? snapRotation : transform.rotation;

        SnapAndFreeze(position, rotation);
        _deliveryHighlightActive.Value = false;
        ApplyDeliveryHighlight(false);
        MarkResolved();
        _isSorted.Value = true;
        LockInteractableNetworked();
        PlaySortSuccessSfx();
    }

    /// <summary>
    /// Server-only. Immediately zeroes this package's Rigidbody velocity, makes it kinematic,
    /// and snaps its transform to the given world pose on every client — used so a package that
    /// is still physically flying (thrown) when it correctly lands in a slot comes to rest
    /// exactly in place instead of continuing to travel with its throw momentum after its
    /// collider is disabled.
    /// </summary>
    private void SnapAndFreeze(Vector3 position, Quaternion rotation)
    {
        ApplySnapAndFreeze(position, rotation);
        SnapAndFreezeClientRpc(position, rotation);
    }

    [ClientRpc]
    private void SnapAndFreezeClientRpc(Vector3 position, Quaternion rotation) => ApplySnapAndFreeze(position, rotation);

    private void ApplySnapAndFreeze(Vector3 position, Quaternion rotation)
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        transform.position = position;
        transform.rotation = rotation;
    }

    /// <summary>Server-only. Marks this package as resolved so it cannot be counted twice.</summary>
    public void MarkResolved() => IsResolved = true;

    /// <summary>
    /// Server-only. Restores normal server-authoritative physics after this package has been
    /// driven along a scripted arc (see <see cref="MailSortBin.InteractWithItem"/>'s toss-in
    /// mechanic) — re-enables NetworkTransform, makes the Rigidbody non-kinematic again with zero
    /// velocity, and positions it at the arc's landing point on every client. Mirrors
    /// <see cref="ThrowServerRpc"/>/its ClientRpc, minus an actual throw velocity. Must be called
    /// right before <see cref="SortMailTask.EvaluateSort"/> so that a package rejected from the
    /// wrong bin can bounce out with a real physics impulse (<see cref="RejectFromBin"/>) instead
    /// of staying kinematic/frozen from the scripted toss — a correct sort simply re-freezes it
    /// again immediately afterward via <see cref="ResolveAndSettle"/>.
    /// </summary>
    public void ResumePhysicsAfterScriptedThrow(Vector3 landPosition)
    {
        if (!IsServer) return;

        transform.position = landPosition;
        NetworkObject.RemoveOwnership();

        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        ResumePhysicsAfterScriptedThrowClientRpc(landPosition);
    }

    [ClientRpc]
    private void ResumePhysicsAfterScriptedThrowClientRpc(Vector3 landPosition)
    {
        transform.position = landPosition;
        NetworkObject.AutoObjectParentSync = true;

        NetworkTransform nt = GetComponent<NetworkTransform>();
        if (nt != null) nt.enabled = true;

        if (IsServer) return;
        if (_rb != null) _rb.isKinematic = true;
    }

    /// <summary>
    /// Server-only. Plays <see cref="_sortSuccessSfxClip"/> on every client. Called by
    /// <see cref="ResolveAndSettle"/> for both a correct cubby delivery (<see cref="MarkDelivered"/>)
    /// and a correct Confiscate sort (<see cref="MarkConfiscated"/>).
    /// </summary>
    public void PlaySortSuccessSfx()
    {
        if (!IsServer) return;
        PlaySortSuccessSfxClientRpc();
    }

    [ClientRpc]
    private void PlaySortSuccessSfxClientRpc()
    {
        if (_sortSuccessSfxClip != null)
            SFXController.Instance?.PlayAtPosition(_sortSuccessSfxClip, transform.position, _sortSuccessSfxVolume);
    }

    /// <summary>
    /// Called by <see cref="MailSortBin"/> or <see cref="MailCubbySlot"/> (any client) when this
    /// package is dropped into a bin or cubby slot. Routes the sort attempt to the server, which
    /// owns <see cref="SortMailTask"/> and decides whether the placement was correct.
    /// </summary>
    /// <param name="binType">Which sorting outcome the package was dropped into.</param>
    /// <param name="slotResidentPoolIndex">
    /// When <paramref name="binType"/> is <see cref="MailSortBinType.Delivery"/>, the resident
    /// pool index (<see cref="MailCubbySlot.ResidentPoolIndex"/>) of the resident assigned to the
    /// specific cubby slot the package was dropped into. The server resolves this back to the
    /// actual <see cref="SuspectData"/> reference (see <see cref="MailCubbyManager.ResolveResident"/>)
    /// and compares it directly against <see cref="AssignedResident"/> — see
    /// <see cref="SortMailTask.EvaluateSort"/>. Ignored for Confiscate. -1 if dropped into a
    /// generic bin rather than a labelled cubby.
    /// </param>
    /// <param name="hasSnapPose">
    /// True if the caller (a <see cref="MailCubbySlot"/>) supplied a fixed placement pose this
    /// package should snap to if the sort turns out to be correct — see
    /// <see cref="MarkDelivered"/>/<see cref="MarkConfiscated"/>. Always false for a generic
    /// <see cref="MailSortBin"/> trigger drop, which freezes only after the bin confirms the
    /// package has fully entered and settled.
    /// </param>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSortServerRpc(int binType, int slotResidentPoolIndex = -1, bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        SortMailTask.Instance?.EvaluateSort(this, (MailSortBinType)binType, slotResidentPoolIndex, hasSnapPose, snapPosition, snapRotation);
    }

    /// <summary>
    /// Server-only. Gives the package a gentle random kick so it visibly bounces out of the wrong
    /// bin instead of silently sitting inside it. Relies on the existing NetworkRigidbody to
    /// replicate the resulting motion to clients.
    /// </summary>
    public void RejectFromBin(Vector3 awayDirection, float upForce = 2.5f, float outForce = 1.5f)
    {
        if (!IsServer) return;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) return;

        Vector3 kick = Vector3.up * upForce + awayDirection.normalized * outForce;
        rb.AddForce(kick, ForceMode.Impulse);
    }

    private void RefreshLabel()
    {
        if (_residentNameText != null)
            _residentNameText.text = ResidentName;

        if (_goodsLabelText != null)
            _goodsLabelText.text = GoodsLabel;

        interactText = string.IsNullOrEmpty(ResidentName) ? "Package" : $"Package — {ResidentName} ({GoodsLabel})";
    }
}
