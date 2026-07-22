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

    /// <summary>Server-only. Marks this package as resolved so it cannot be counted twice.</summary>
    public void MarkResolved() => IsResolved = true;

    /// <summary>
    /// Called by <see cref="MailSortBin"/> (any client) when this package is dropped into a bin.
    /// Routes the sort attempt to the server, which owns <see cref="SortMailTask"/> and decides
    /// whether the placement was correct.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestSortServerRpc(int binType)
    {
        SortMailTask.Instance?.EvaluateSort(this, (MailSortBinType)binType);
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
