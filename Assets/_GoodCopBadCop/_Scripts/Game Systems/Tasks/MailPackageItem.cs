using Unity.Collections;
using Unity.Netcode;
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

    // ── Networked data, set once by the server at spawn time via ServerInitialize ──────────────

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

    /// <summary>True once this package has been correctly sorted and is pending despawn. Server-only guard against double-counting.</summary>
    public bool IsResolved { get; private set; }

    public string ResidentName => _residentName.Value.ToString();
    public string GoodsLabel   => _goodsLabel.Value.ToString();
    public MailSortBinType CorrectBin => (MailSortBinType)_correctBin.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _residentName.OnValueChanged += (_, _) => RefreshLabel();
        _goodsLabel.OnValueChanged   += (_, _) => RefreshLabel();
        RefreshLabel();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
    }

    /// <summary>
    /// Server-only. Assigns this package's addressee, goods category, and correct sorting bin.
    /// Call immediately after spawning, before the object is observed by clients if possible.
    /// </summary>
    public void ServerInitialize(string residentName, string goodsLabel, MailSortBinType correctBin)
    {
        if (!IsServer) return;

        _residentName.Value = residentName;
        _goodsLabel.Value   = goodsLabel;
        _correctBin.Value   = (int)correctBin;
        IsResolved          = false;

        RefreshLabel();
    }

    /// <summary>
    /// Server-only. Marks this package as resolved, permanently locks it so it can no longer be
    /// picked up (via <see cref="LockInteractableNetworked"/>), and plays the delivery sound
    /// effect on every client. Called by <see cref="SortMailTask.EvaluateSort"/> when this
    /// package is dropped into its addressee's mailbox cubby — unlike a Confiscate correct sort,
    /// delivered packages are not despawned immediately; they stay sitting in the mailbox until
    /// <see cref="SortMailTask.DespawnDeliveredPackages"/> clears them at the start of the next
    /// day.
    ///
    /// If <paramref name="hasSnapPose"/> is true, the package is first snapped to
    /// <paramref name="snapPosition"/>/<paramref name="snapRotation"/> (the cubby's
    /// <see cref="PlacementSlot"/> pose) and its Rigidbody is zeroed out and made kinematic
    /// before colliders are disabled — otherwise a package that was thrown into the slot keeps
    /// its throw velocity after its collider is locked out, and with no collider left to stop it
    /// it flies straight through the floor and is lost.
    /// </summary>
    public void MarkDelivered(bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        if (!IsServer) return;

        if (hasSnapPose)
            SnapAndFreeze(snapPosition, snapRotation);

        MarkResolved();
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
    /// Server-only. Plays <see cref="_sortSuccessSfxClip"/> on every client. Called by
    /// <see cref="MarkDelivered"/> for a correct cubby delivery, and directly by
    /// <see cref="SortMailTask.EvaluateSort"/> for a correct Confiscate sort (which despawns the
    /// package immediately rather than going through MarkDelivered).
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
    /// <param name="slotResidentName">
    /// When <paramref name="binType"/> is <see cref="MailSortBinType.Delivery"/>, the resident
    /// name assigned to the specific cubby slot the package was dropped into (see
    /// <see cref="MailCubbySlot"/>). Ignored for Confiscate. Empty if dropped into a generic bin
    /// rather than a labelled cubby.
    /// </param>
    /// <param name="hasSnapPose">
    /// True if the caller (a <see cref="MailCubbySlot"/>) supplied a fixed placement pose this
    /// package should snap to if the sort turns out to be correct — see
    /// <see cref="MarkDelivered"/>. Always false for a generic <see cref="MailSortBin"/>, since a
    /// correctly-sorted Confiscate package is despawned immediately rather than snapped in place.
    /// </param>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSortServerRpc(int binType, string slotResidentName = "", bool hasSnapPose = false, Vector3 snapPosition = default, Quaternion snapRotation = default)
    {
        SortMailTask.Instance?.EvaluateSort(this, (MailSortBinType)binType, slotResidentName, hasSnapPose, snapPosition, snapRotation);
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
