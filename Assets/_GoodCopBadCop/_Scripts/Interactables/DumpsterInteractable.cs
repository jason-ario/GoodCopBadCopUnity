using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Dumpster for the Trash Build-up systemic threat.
///
/// A TrashBag can be deposited in two ways:
///   1. Thrown with real physics (ThrowController's F-key charge-and-release throw) directly
///      into the dumpster's opening — a child DumpsterPhysicsDepositZone trigger volume
///      detects the in-flight bag and calls <see cref="TryDepositThrownBag"/>.
///   2. Interacted with directly while holding a TrashBag (left-click / E, routed by
///      <see cref="PlayerInteractionController"/> via <see cref="InteractWithItem"/> because
///      the Trash Bag PickableItemData is listed in <c>itemsThatCanInteractWith</c>) — this
///      plays the player's throw animation and tosses the bag along a scripted parabolic arc
///      (<see cref="ThrowBagArcServerRpc"/>/<see cref="ThrowBagArcClientRpc"/>) into the
///      dumpster's opening before it despawns and is registered as deposited.
///
/// Each TrashBag instance can only be deposited once — <see cref="TrashBag.IsDeposited"/>
/// is checked and set atomically before any further processing so a bag can never be
/// registered twice (e.g. from overlapping trigger events in the same physics step, or a
/// throw landing at the same moment as a manual interact).
///
/// A world-space label hovers above the dumpster and is visible only while the player looks
/// at it. It shows the current deposited count.
///
/// Prefab setup:
///   - NetworkObject + HighlightEffect + Collider (Interactable layer)
///   - A child GameObject with a trigger Collider + DumpsterPhysicsDepositZone, positioned
///     inside the opening, to catch bags thrown in with physics
///   - Trash Bag PickableItemData added to itemsThatCanInteractWith for the manual deposit flow
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

    [Header("Throw Arc")]
    [Tooltip("Point inside the dumpster's opening the bag arcs toward before despawning. " +
             "Auto-resolved from a child DumpsterPhysicsDepositZone if left empty, falling " +
             "back to this dumpster's own position.")]
    [SerializeField] private Transform _depositTarget;

    [Tooltip("Duration in seconds of the toss arc from the player's hand into the dumpster.")]
    [SerializeField] private float _throwArcDuration = 0.5f;

    [Tooltip("Extra height added at the arc's midpoint, on top of the straight line between start and end.")]
    [SerializeField] private float _throwArcHeight = 1.5f;

    [Tooltip("Animator trigger played on the player (body + arms) when tossing a bag into the dumpster.")]
    [SerializeField] private string _throwAnimTrigger = "ThrowTrashBag";

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        if (_labelRoot != null)
            _labelRoot.SetActive(false);

        if (_depositTarget == null)
        {
            DumpsterPhysicsDepositZone zone = GetComponentInChildren<DumpsterPhysicsDepositZone>();
            if (zone != null)
                _depositTarget = zone.transform;
        }
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

    // ── Interact-based deposit (holding a TrashBag) ──────────────────────────

    /// <summary>
    /// Called by <see cref="PlayerInteractionController"/> on the local client when the player
    /// interacts with the dumpster while holding a <see cref="TrashBag"/> (the Trash Bag
    /// PickableItemData must be listed in <c>itemsThatCanInteractWith</c> on the prefab).
    /// Plays the player's throw animation, releases the bag from their hand, and kicks off a
    /// networked toss arc into the dumpster's opening. The bag despawns and is registered as
    /// deposited only once the arc completes (see <see cref="ThrowBagArcServerRpc"/>).
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        if (item is not TrashBag bag || bag.IsDeposited) return;

        base.InteractWithItem(playerInteractionController, item);

        PlayerAnimationController animController = playerInteractionController.pickupController.PlayerAnimationController;
        if (animController != null)
            animController.SetAnimTrigger(_throwAnimTrigger);

        Vector3 startPosition = bag.transform.position;

        // Release the bag from the player's hand (skips DropServerRpc so the bag is not
        // re-enabled as an interactable/NetworkTransform before the arc tween takes over).
        playerInteractionController.pickupController.ReleaseHeldObjectForThrow();

        Vector3 endPosition = _depositTarget != null ? _depositTarget.position : transform.position;

        ThrowBagArcServerRpc(bag.NetworkObject, startPosition, endPosition);
    }

    /// <summary>
    /// Server-side entry point for a bag tossed into the dumpster via <see cref="InteractWithItem"/>.
    /// Resolves the bag by NetworkObjectReference and, if it is still valid and not already
    /// deposited, marks it deposited immediately (so it can never be registered twice), then
    /// broadcasts the visual arc to every client and schedules the actual despawn/registration
    /// for when the arc finishes.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ThrowBagArcServerRpc(NetworkObjectReference bagRef, Vector3 startPosition, Vector3 endPosition)
    {
        if (!bagRef.TryGet(out NetworkObject bagNetworkObject)) return;

        TrashBag bag = bagNetworkObject.GetComponent<TrashBag>();
        if (bag == null || !bag.IsSpawned || bag.IsDeposited) return;

        bag.MarkDeposited();

        ThrowBagArcClientRpc(bagRef, startPosition, endPosition);
        StartCoroutine(FinishArcDeposit(bag, endPosition));
    }

    /// <summary>
    /// Received on all clients (including the server). Plays the visual toss arc on the bag's
    /// transform. NetworkTransform is already disabled on this bag (left disabled by
    /// <see cref="PlayerPickupController.ReleaseHeldObjectForThrow"/>), so it is safe for every
    /// client to drive the bag's position locally and in lockstep.
    /// </summary>
    [ClientRpc]
    private void ThrowBagArcClientRpc(NetworkObjectReference bagRef, Vector3 startPosition, Vector3 endPosition)
    {
        if (!bagRef.TryGet(out NetworkObject bagNetworkObject)) return;

        StartCoroutine(AnimateThrowArc(bagNetworkObject.transform, startPosition, endPosition));
    }

    /// <summary>Moves <paramref name="bagTransform"/> along a simple parabolic arc.</summary>
    private IEnumerator AnimateThrowArc(Transform bagTransform, Vector3 startPosition, Vector3 endPosition)
    {
        float elapsed = 0f;
        while (elapsed < _throwArcDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _throwArcDuration);

            Vector3 point = Vector3.Lerp(startPosition, endPosition, t);
            point.y += _throwArcHeight * Mathf.Sin(t * Mathf.PI);

            if (bagTransform == null) yield break;
            bagTransform.position = point;

            yield return null;
        }

        if (bagTransform != null)
            bagTransform.position = endPosition;
    }

    /// <summary>
    /// Server-only: waits for the arc duration to elapse, then finalizes the deposit — plays
    /// the land sound, despawns the bag, and increments the fill counter — the same way a
    /// physics-thrown bag is finalized.
    /// </summary>
    private IEnumerator FinishArcDeposit(TrashBag bag, Vector3 landPosition)
    {
        yield return new WaitForSeconds(_throwArcDuration);

        int junkCount = bag.JunkCount;

        PlayLandSoundServerRpc(landPosition);
        bag.DespawnServerRpc();
        DepositBagServerRpc(junkCount);
    }

    // ── Physics throw deposit ────────────────────────────────────────────────

    /// <summary>
    /// Called by a <see cref="DumpsterPhysicsDepositZone"/> when a TrashBag thrown with real
    /// physics flies into the dumpster's opening.
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

        DepositBag(bag);
        return true;
    }

    /// <summary>
    /// Shared server-only deposit routine used by the physics-throw path
    /// (<see cref="TryDepositThrownBag"/>). Marks the bag deposited immediately, before any
    /// further processing, so this exact bag instance can never be registered again even if
    /// another trigger fires for it in the same frame.
    /// </summary>
    private void DepositBag(TrashBag bag)
    {
        bag.MarkDeposited();

        int     junkCount    = bag.JunkCount;
        Vector3 landPosition = bag.transform.position;

        PlayLandSoundServerRpc(landPosition);
        bag.DespawnServerRpc();
        DepositBagServerRpc(junkCount);
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
