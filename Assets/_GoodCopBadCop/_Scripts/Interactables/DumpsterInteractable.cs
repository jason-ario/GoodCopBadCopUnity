using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dumpster for the Trash Build-up systemic threat.
///
/// The ONLY way to deposit a TrashBag is to throw it with real physics (ThrowController's
/// F-key charge-and-release throw) directly into the dumpster's opening — a child
/// DumpsterPhysicsDepositZone trigger volume detects the in-flight bag and calls
/// <see cref="TryDepositThrownBag"/>, which validates and deposits it. There is no
/// left-click / interact-based deposit flow; the dumpster does not accept items via
/// InteractWithItem.
///
/// Each TrashBag instance can only be deposited once — <see cref="TrashBag.IsDeposited"/>
/// is checked and set atomically in <see cref="TryDepositThrownBag"/> so a bag can never be
/// registered twice (e.g. from overlapping trigger events in the same physics step).
///
/// A world-space label hovers above the dumpster and is visible only while the player looks
/// at it. It shows the current deposited count.
///
/// Prefab setup:
///   - NetworkObject + HighlightEffect + Collider (Interactable layer)
///   - A child GameObject with a trigger Collider + DumpsterPhysicsDepositZone, positioned
///     inside the opening, to catch bags thrown in with physics
/// </summary>
public class DumpsterInteractable : CollectableContainer
{
    /// <summary>
    /// Fired on the server when a TrashBag is successfully deposited.
    /// The parameter is the number of junk items that were in the bag.
    /// </summary>
    public static event Action<int> OnTrashBagDeposited;

    [Header("World Label")]
    [Tooltip("The world-space Canvas GO that contains the label. Toggled on reticle hover.")]
    [SerializeField] private GameObject _labelRoot;
    [Tooltip("The TextMeshProUGUI on the label canvas.")]
    [SerializeField] private TextMeshProUGUI _labelText;

    [Header("Audio")]
    [SerializeField] private AudioClip _depositSound;
    [SerializeField] private float _depositSoundVolume = 1f;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        if (_labelRoot != null)
            _labelRoot.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        RefreshLabel();
    }

    private void LateUpdate()
    {
        // Billboard: keep the label facing the camera while it's visible.
        if (_labelRoot != null && _labelRoot.activeSelf && Camera.main != null)
        {
            _labelRoot.transform.LookAt(Camera.main.transform.position);
            _labelRoot.transform.Rotate(0f, 180f, 0f);
        }
    }

    // ── Highlight callbacks (called by PlayerInteractionController) ───────────

    protected override void OnHighlight()
    {
        if (_labelRoot != null)
            _labelRoot.SetActive(true);
    }

    protected override void OnStopHighlight()
    {
        if (_labelRoot != null)
            _labelRoot.SetActive(false);
    }

    // ── Capacity override ─────────────────────────────────────────────────────

    /// <summary>Dumpsters have no fill limit — always accept more bags.</summary>
    public override bool IsFull => false;

    // ── Deposit ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Increments this dumpster's fill counter and fires <see cref="OnTrashBagDeposited"/>
    /// with the number of junk items that were in the deposited bag. Server-only.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void DepositBagServerRpc(int junkCount)
    {
        PerformDeposit();
        if (junkCount > 0)
            OnTrashBagDeposited?.Invoke(junkCount);
    }

    // ── Physics throw deposit ────────────────────────────────────────────────

    /// <summary>
    /// Called by a <see cref="DumpsterPhysicsDepositZone"/> when a TrashBag thrown with real
    /// physics flies into the dumpster's opening. This is the only way a TrashBag is ever
    /// deposited — the dumpster has no interact-based deposit flow.
    ///
    /// Only a bag that is actually in free physics flight (Rigidbody non-kinematic, as set by
    /// <see cref="PickableObject.ThrowServerRpc"/>) is accepted, so bags merely resting nearby
    /// or still held/carried do not get deposited by walking past. A bag that has already been
    /// deposited (<see cref="TrashBag.IsDeposited"/>) is rejected, so the same bag instance can
    /// never be registered more than once even if multiple trigger events fire for it.
    ///
    /// Runs only on the server; safe to call from any client's local trigger callback.
    /// </summary>
    /// <returns>True if the bag was deposited.</returns>
    public bool TryDepositThrownBag(TrashBag bag)
    {
        if (!IsServer) return false;
        if (bag == null || !bag.IsSpawned || bag.IsDeposited) return false;

        Rigidbody rb = bag.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return false;

        // Mark deposited immediately, before any further processing, so this exact bag
        // instance can never be registered again even if another trigger fires for it
        // in the same frame.
        bag.MarkDeposited();

        int     junkCount    = bag.JunkCount;
        Vector3 landPosition = bag.transform.position;

        PlayLandSoundServerRpc(landPosition);
        bag.DespawnServerRpc();
        DepositBagServerRpc(junkCount);

        return true;
    }

    // ── NetworkVariable callbacks ─────────────────────────────────────────────

    protected override void OnFillCountChanged(int previous, int current)
    {
        base.OnFillCountChanged(previous, current); // calls RefreshInteractText
        RefreshLabel();
    }

    protected override void OnPickupStateChanged(bool previous, bool current)
    {
        base.OnPickupStateChanged(previous, current); // calls RefreshInteractText
        RefreshLabel();
    }

    // ── Interact text ─────────────────────────────────────────────────────────

    protected override string GetDefaultInteractText() => "Dumpster";
    protected override string GetFullInteractText()    => "Dumpster";

    // ── World-space label ─────────────────────────────────────────────────────

    private void RefreshLabel()
    {
        if (_labelText == null) return;

        _labelText.text  = $"{FillCount}";
        _labelText.color = Color.white;
    }

    // ── Audio ─────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void PlayLandSoundServerRpc(Vector3 position)
    {
        PlayLandSoundClientRpc(position);
    }

    [ClientRpc]
    private void PlayLandSoundClientRpc(Vector3 position)
    {
        if (SFXController.Instance != null && _depositSound != null)
            SFXController.Instance.PlayAtPosition(_depositSound, position, _depositSoundVolume);
    }
}
