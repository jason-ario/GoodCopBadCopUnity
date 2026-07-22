using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable dumpster for the Trash Build-up systemic threat.
///
/// Left-clicking the dumpster while holding a TrashBag triggers the throw sequence:
///   1. Player controls are locked.
///   2. A throw animation trigger is fired on the player animator (synced to all clients).
///   3. ReleaseHeldObjectForThrow() detaches the bag without calling DropServerRpc,
///      keeping NetworkTransform disabled so DOTween retains full control.
///   4. DOJump arcs the real bag into the dumpster.
///   5. The bag is despawned and controls are restored.
///
/// Once full, left-clicking with no held item calls HQ for pickup (from CollectableContainer.Interact).
///
/// Bags can also be deposited by throwing them with real physics (ThrowController's F-key
/// charge-and-release throw) directly into the dumpster's opening — a child
/// DumpsterPhysicsDepositZone trigger volume detects the in-flight bag and calls
/// <see cref="TryDepositThrownBag"/>, which deposits it immediately without the scripted
/// windup/animation sequence used by the left-click interact flow.
///
/// A world-space label hovers above the dumpster and is visible only while the player looks
/// at it. It shows "X/Capacity" in white, "FULL" in red, or "PICKUP REQUESTED" in yellow.
///
/// Prefab setup:
///   - NetworkObject + HighlightEffect + Collider (Interactable layer)
///   - Trash Bag PickableItemData assigned to itemsThatCanInteractWith
///   - Three child Transforms assigned to _throwTargets (positions inside the dumpster opening)
///   - A child GameObject with a trigger Collider + DumpsterPhysicsDepositZone, positioned
///     inside the opening, to catch bags thrown in with physics
/// </summary>
public class DumpsterInteractable : CollectableContainer
{
    private const string ThrowAnimTrigger = "ThrowTrashBag";

    /// <summary>
    /// Fired on the server when a TrashBag is successfully deposited.
    /// The parameter is the number of junk items that were in the bag.
    /// </summary>
    public static event Action<int> OnTrashBagDeposited;

    [Header("Throw Targets")]
    [Tooltip("Three positions inside the dumpster opening. One is chosen at random per throw.")]
    [SerializeField] private Transform[] _throwTargets = new Transform[3];

    [Header("Throw Settings")]
    [Tooltip("Seconds after the animation trigger fires before the bag visually leaves the hand.")]
    [SerializeField] private float _throwWindupDelay = 0.15f;
    [Tooltip("Peak height of the throw arc above the straight-line path.")]
    [SerializeField] private float _jumpHeight = 1.5f;
    [Tooltip("Total duration of the throw arc in seconds.")]
    [SerializeField] private float _jumpDuration = 0.45f;
    [SerializeField] private Ease _jumpEase = Ease.Linear;

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

    // ── Interact ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called via left-click while holding a TrashBag.
    /// The Trash Bag PickableItemData must be listed in itemsThatCanInteractWith on the prefab.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject item)
    {
        TrashBag bag = item as TrashBag;
        if (bag == null) return;

        base.InteractWithItem(player, item);
        StartCoroutine(ThrowSequence(player, bag));
    }

    // ── Throw sequence ───────────────────────────────────────────────────────

    private IEnumerator ThrowSequence(PlayerInteractionController player, TrashBag bag)
    {
        PlayerMovementController movement = player.playerMovementController;
        PlayerAnimationController anim    = player.playerAnimationController;

        // ── 1. Lock controls ─────────────────────────────────────────────────
        movement.SetCanMove(false);
        movement.SetCanLook(false);
        player.SetCanInteract(false, string.Empty);

        // ── 2. Fire throw animation (synced to all clients) ──────────────────
        anim.SetAnimTrigger(ThrowAnimTrigger);
        anim.SetAnimBool("HoldingTrashBag", false);

        // ── 3. Pick throw target ──────────────────────────────────────────────
        Transform target     = PickThrowTarget();
        Vector3 landPosition = target.position;

        // ── 4. Release the bag from the player's hand ─────────────────────────
        TrashBag depositedBag = bag;
        int junkCount = bag.JunkCount; // capture before despawn clears it
        player.pickupController.ReleaseHeldObjectForThrow();

        // ── 5. Windup delay before the bag visually moves ─────────────────────
        yield return new WaitForSeconds(_throwWindupDelay);

        // ── 6. Broadcast the throw arc to ALL clients ─────────────────────────
        depositedBag.PlayThrowArcClientRpc(landPosition, _jumpHeight, _jumpDuration, (int)_jumpEase);
        yield return new WaitForSeconds(_jumpDuration);

        // ── 7. Deposit feedback — networked so all clients hear the land sound ──
        PlayLandSoundServerRpc(landPosition);

        // ── 8. Despawn the bag ────────────────────────────────────────────────
        depositedBag.DespawnServerRpc();

        // ── 9. Increment the fill counter and notify task listeners ───────────
        DepositBagServerRpc(junkCount);

        // ── 10. Restore player controls ───────────────────────────────────────
        movement.SetCanMove(true);
        movement.SetCanLook(true);
        player.SetCanInteract(true, string.Empty);
    }

    /// <summary>Picks a random non-null entry from _throwTargets; falls back to this transform.</summary>
    private Transform PickThrowTarget()
    {
        var valid = System.Array.FindAll(_throwTargets, t => t != null);

        if (valid.Length == 0)
        {
            Debug.LogWarning("[DumpsterInteractable] No throw targets assigned — using dumpster centre.");
            return transform;
        }

        return valid[UnityEngine.Random.Range(0, valid.Length)];
    }

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
    /// physics (via ThrowController, not the scripted left-click throw sequence) flies into
    /// the dumpster's opening. Deposits the bag immediately — no windup, animation, or
    /// player-control locking, since the player is not necessarily interacting with this
    /// dumpster directly.
    ///
    /// Only a bag that is actually in free physics flight (Rigidbody non-kinematic, as set by
    /// <see cref="PickableObject.ThrowServerRpc"/>) is accepted, so bags merely resting nearby
    /// or still held/carried do not get deposited by walking past.
    ///
    /// Runs only on the server; safe to call from any client's local trigger callback.
    /// </summary>
    /// <returns>True if the bag was deposited.</returns>
    public bool TryDepositThrownBag(TrashBag bag)
    {
        if (!IsServer) return false;
        if (bag == null || !bag.IsSpawned) return false;

        Rigidbody rb = bag.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return false;

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

    // ── Editor gizmos ─────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (_throwTargets == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        foreach (Transform t in _throwTargets)
        {
            if (t != null)
                Gizmos.DrawWireSphere(t.position, 0.15f);
        }
    }
#endif
}
