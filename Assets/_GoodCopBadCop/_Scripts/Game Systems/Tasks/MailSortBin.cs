using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Placed on the generic mail bin ("Mail Bin - Confiscate"). Detects a
/// <see cref="MailPackageItem"/> being physically dropped inside via a trigger collider and
/// forwards the sort attempt to the server. Also supports an interact-based deposit, mirroring
/// <see cref="DumpsterInteractable"/>: the player can walk up to the bin and interact (LMB or E)
/// while holding a package to toss it in along a scripted arc, instead of relying on the player's
/// own physics throw.
///
/// There is no generic "Mail Bin - Delivery": deliverable packages must instead be dropped into
/// the addressee's own cubby — see <see cref="MailCubbySlot"/> on the "Mail Cubbies" prefab.
///
/// Setup:
///   - NetworkObject + HighlightEffect on this GameObject (required by <see cref="Interactable"/>).
///   - Assign <see cref="_binType"/> to match this bin's label (Confiscate).
///   - Assign <see cref="_triggerZone"/> to a Collider (isTrigger = true) covering the bin's
///     opening. If left unassigned, this component falls back to the first Collider found on
///     this GameObject — make sure that collider is marked as a trigger, or add a dedicated
///     child trigger volume so the bin's solid mesh collider is left untouched.
///   - IMPORTANT: Unity only delivers OnTriggerEnter to components on the SAME GameObject as the
///     trigger Collider — it never bubbles up to a parent. If <see cref="_triggerZone"/> lives on
///     a child GameObject (e.g. a dedicated "Trigger Zone" object) rather than this GameObject
///     itself, add a <see cref="MailSortBinTriggerRelay"/> component to that child so trigger
///     events still reach this <see cref="MailSortBin"/>.
///   - Add the Mail Package's PickableItemData asset (e.g. "Small Package.asset") to
///     <see cref="Interactable.itemsThatCanInteractWith"/> so <see cref="InteractWithItem"/> is
///     actually routed to this bin while the player holds a package.
///   - For the raycast reticle/highlight to pick up this bin, add an <see cref="InteractableCollider"/>
///     to the bin's solid (non-trigger) mesh collider, pointing back at this component — see
///     <see cref="DumpsterInteractable"/>'s prefab for reference.
/// </summary>
public class MailSortBin : Interactable
{
    [Tooltip("Which sorting outcome this bin represents.")]
    [SerializeField] private MailSortBinType _binType;

    [Tooltip("Trigger collider covering the bin's opening. Falls back to GetComponent<Collider>() if unassigned.")]
    [SerializeField] private Collider _triggerZone;

    [Header("Interact Toss")]
    [Tooltip("Point inside the bin's opening the package arcs toward before landing. Auto-resolved " +
             "from _triggerZone's bounds center if left empty, falling back to this bin's own position.")]
    [SerializeField] private Transform _depositTarget;

    [Tooltip("Duration in seconds of the toss arc from the player's hand into the bin.")]
    [SerializeField] private float _throwArcDuration = 0.5f;

    [Tooltip("Extra height added at the arc's midpoint, on top of the straight line between start and end.")]
    [SerializeField] private float _throwArcHeight = 1.5f;

    [Tooltip("Animator trigger played on the player (body + arms) when tossing a package into this bin. " +
             "Leave empty to skip the throw animation.")]
    [SerializeField] private string _throwAnimTrigger = "";

    [Header("Confiscate Settlement")]
    [Tooltip("Minimum clearance between the package bounds and the trigger volume edge before a contraband package can settle. Prevents a package caught on the bin lip from locking there.")]
    [SerializeField, Min(0f)] private float _interiorClearance = 0.08f;

    [Tooltip("Maximum linear speed (m/s) a fully-inside contraband package may have while it is considered settled.")]
    [SerializeField, Min(0f)] private float _settleLinearSpeed = 0.12f;

    [Tooltip("Maximum angular speed (rad/s) a fully-inside contraband package may have while it is considered settled.")]
    [SerializeField, Min(0f)] private float _settleAngularSpeed = 0.35f;

    [Tooltip("How long a contraband package must remain fully inside and below the motion thresholds before it locks in place.")]
    [SerializeField, Min(0f)] private float _settleDuration = 0.35f;

    private readonly HashSet<MailPackageItem> _pendingSettlements = new();

    public MailSortBinType BinType => _binType;

    protected override void Awake()
    {
        base.Awake();

        if (_triggerZone == null)
            _triggerZone = GetComponent<Collider>();

        if (_triggerZone == null)
            Debug.LogWarning($"[MailSortBin] '{name}' has no trigger collider assigned or attached.", this);
        else if (!_triggerZone.isTrigger)
            Debug.LogWarning($"[MailSortBin] '{name}' trigger collider is not marked isTrigger — packages will not be detected.", this);
        else if (_triggerZone.gameObject != gameObject && _triggerZone.GetComponent<MailSortBinTriggerRelay>() == null)
            Debug.LogWarning($"[MailSortBin] '{name}' trigger collider lives on a different GameObject ('{_triggerZone.name}') " +
                              "without a MailSortBinTriggerRelay component — OnTriggerEnter will never fire on this MailSortBin. " +
                              "Add a MailSortBinTriggerRelay to the trigger collider's GameObject.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandlePackageTriggerEnter(other);
    }

    /// <summary>
    /// Evaluates whether the given Collider belongs to a droppable <see cref="MailPackageItem"/>
    /// and, if so, forwards the sort attempt to the server. Called directly by
    /// <see cref="OnTriggerEnter"/> when <see cref="_triggerZone"/> is on this GameObject, or by
    /// <see cref="MailSortBinTriggerRelay"/> when the trigger collider lives on a child instead.
    /// </summary>
    public void HandlePackageTriggerEnter(Collider other)
    {
        MailPackageItem package = other.GetComponentInParent<MailPackageItem>();
        if (package == null) return;
        // NOTE: see the matching comment in MailCubbySlot.OnTriggerEnter — do NOT gate on
        // package.IsHeld. Held packages have their colliders disabled, so this can only ever see
        // a package that was just dropped; IsHeld itself can still read stale/true here because
        // it's a server-authoritative NetworkVariable that hasn't round-tripped back yet.
        if (package.IsResolved) return;

        if (_binType == MailSortBinType.Confiscate)
        {
            NetworkObject binNetworkObject = GetComponent<NetworkObject>();
            if (binNetworkObject != null)
                package.RequestConfiscateSettleServerRpc(new NetworkObjectReference(binNetworkObject));
            return;
        }

        package.RequestSortServerRpc((int)_binType, -1);
    }

    /// <summary>
    /// Server-only. Correct contraband remains a dynamic physics object until every corner of its
    /// bounds has moved beyond the trigger volume's inset and it has stayed still for the configured
    /// duration. Leaving the volume cancels the pending deposit; a later re-entry begins a fresh
    /// observation window.
    /// </summary>
    public void BeginPackageSettlement(MailPackageItem package)
    {
        if (!IsServer || package == null || package.IsResolved) return;

        if (package.CorrectBin != MailSortBinType.Confiscate)
        {
            SortMailTask.Instance?.EvaluateSort(package, _binType);
            return;
        }

        if (_pendingSettlements.Add(package))
            StartCoroutine(WaitForPackageSettlement(package));
    }

    private IEnumerator WaitForPackageSettlement(MailPackageItem package)
    {
        float settledTime = 0f;
        Rigidbody packageRigidbody = package != null ? package.GetComponent<Rigidbody>() : null;

        while (package != null && package.IsSpawned && !package.IsResolved)
        {
            if (!IsPackageComfortablyInside(package))
            {
                _pendingSettlements.Remove(package);
                yield break;
            }

            bool isStill = packageRigidbody == null ||
                           (packageRigidbody.linearVelocity.sqrMagnitude <= _settleLinearSpeed * _settleLinearSpeed &&
                            packageRigidbody.angularVelocity.sqrMagnitude <= _settleAngularSpeed * _settleAngularSpeed);
            settledTime = isStill ? settledTime + Time.fixedDeltaTime : 0f;

            if (settledTime >= _settleDuration)
            {
                SortMailTask.Instance?.EvaluateSort(package, _binType);
                break;
            }

            yield return new WaitForFixedUpdate();
        }

        if (package != null)
            _pendingSettlements.Remove(package);
    }

    private bool IsPackageComfortablyInside(MailPackageItem package)
    {
        if (_triggerZone == null) return false;

        Collider packageCollider = package.GetComponent<Collider>();
        if (packageCollider == null) return false;

        Bounds packageBounds = packageCollider.bounds;
        if (_triggerZone is BoxCollider box)
        {
            Vector3 halfSize = box.size * 0.5f - Vector3.one * _interiorClearance;
            if (halfSize.x <= 0f || halfSize.y <= 0f || halfSize.z <= 0f) return false;

            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 worldCorner = packageBounds.center + Vector3.Scale(packageBounds.extents, new Vector3(x, y, z));
                Vector3 localCorner = box.transform.InverseTransformPoint(worldCorner) - box.center;
                if (Mathf.Abs(localCorner.x) > halfSize.x ||
                    Mathf.Abs(localCorner.y) > halfSize.y ||
                    Mathf.Abs(localCorner.z) > halfSize.z)
                    return false;
            }

            return true;
        }

        Bounds innerBounds = _triggerZone.bounds;
        innerBounds.Expand(-_interiorClearance * 2f);
        return innerBounds.Contains(packageBounds.min) && innerBounds.Contains(packageBounds.max);
    }

    // ── Interact-based deposit (holding a MailPackageItem) ────────────────────

    /// <summary>
    /// Resolves the true world-space point the toss arc should land on. Prefers
    /// <see cref="_triggerZone"/>'s bounds center (which correctly bakes in any local
    /// <c>center</c> offset plus the zone's rotation/scale), falling back to
    /// <see cref="_depositTarget"/>'s raw transform position, and finally this bin's own position
    /// if neither is available.
    /// </summary>
    private Vector3 GetDepositWorldPosition()
    {
        if (_triggerZone != null)
            return _triggerZone.bounds.center;

        return _depositTarget != null ? _depositTarget.position : transform.position;
    }

    /// <summary>
    /// Called by <see cref="PlayerInteractionController"/> on the local client when the player
    /// interacts with this bin while holding a <see cref="MailPackageItem"/> (its PickableItemData
    /// must be listed in <c>itemsThatCanInteractWith</c> on the prefab). Plays the player's throw
    /// animation, releases the package from their hand, and kicks off a networked toss arc into
    /// the bin's opening — the sort is only evaluated once the arc lands (see
    /// <see cref="FinishArcDeposit"/>).
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        if (item is not MailPackageItem package || package.IsResolved) return;

        base.InteractWithItem(playerInteractionController, item);

        PlayerAnimationController animController = playerInteractionController.pickupController.PlayerAnimationController;
        if (animController != null && !string.IsNullOrEmpty(_throwAnimTrigger))
            animController.SetAnimTrigger(_throwAnimTrigger);

        Vector3 startPosition = package.transform.position;

        // Release the package from the player's hand (skips DropServerRpc so the package is not
        // re-enabled as an interactable/NetworkTransform before the arc tween takes over).
        playerInteractionController.pickupController.ReleaseHeldObjectForThrow();

        Vector3 endPosition = GetDepositWorldPosition();

        ThrowPackageArcServerRpc(package.NetworkObject, startPosition, endPosition);
    }

    /// <summary>
    /// Server-side entry point for a package tossed into this bin via <see cref="InteractWithItem"/>.
    /// Resolves the package by NetworkObjectReference and, if it is still valid and unresolved,
    /// broadcasts the visual arc to every client and schedules the actual sort evaluation for when
    /// the arc finishes.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ThrowPackageArcServerRpc(NetworkObjectReference packageRef, Vector3 startPosition, Vector3 endPosition)
    {
        if (!packageRef.TryGet(out NetworkObject packageNetworkObject)) return;

        MailPackageItem package = packageNetworkObject.GetComponent<MailPackageItem>();
        if (package == null || !package.IsSpawned || package.IsResolved) return;

        ThrowPackageArcClientRpc(packageRef, startPosition, endPosition);
        StartCoroutine(FinishArcDeposit(package, endPosition));
    }

    /// <summary>
    /// Received on all clients (including the server). Plays the visual toss arc on the package's
    /// transform. NetworkTransform is already disabled on this package (left disabled by
    /// <see cref="PlayerPickupController.ReleaseHeldObjectForThrow"/>), so it is safe for every
    /// client to drive the package's position locally and in lockstep.
    /// </summary>
    [ClientRpc]
    private void ThrowPackageArcClientRpc(NetworkObjectReference packageRef, Vector3 startPosition, Vector3 endPosition)
    {
        if (!packageRef.TryGet(out NetworkObject packageNetworkObject)) return;

        StartCoroutine(AnimateThrowArc(packageNetworkObject.transform, startPosition, endPosition));
    }

    /// <summary>Moves <paramref name="packageTransform"/> along a simple parabolic arc.</summary>
    private IEnumerator AnimateThrowArc(Transform packageTransform, Vector3 startPosition, Vector3 endPosition)
    {
        float elapsed = 0f;
        while (elapsed < _throwArcDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _throwArcDuration);

            Vector3 point = Vector3.Lerp(startPosition, endPosition, t);
            point.y += _throwArcHeight * Mathf.Sin(t * Mathf.PI);

            if (packageTransform == null) yield break;
            packageTransform.position = point;

            yield return null;
        }

        if (packageTransform != null)
            packageTransform.position = endPosition;
    }

    /// <summary>
    /// Server-only: waits for the arc duration to elapse, then restores normal server-authoritative
    /// physics on the package (see <see cref="MailPackageItem.ResumePhysicsAfterScriptedThrow"/>,
    /// needed so a package rejected from the wrong bin can bounce out with a real physics impulse)
    /// before finally evaluating the sort — the same way a directly-thrown package is evaluated by
    /// <see cref="HandlePackageTriggerEnter"/>.
    /// </summary>
    private IEnumerator FinishArcDeposit(MailPackageItem package, Vector3 landPosition)
    {
        yield return new WaitForSeconds(_throwArcDuration);

        if (package == null || !package.IsSpawned || package.IsResolved) yield break;

        package.ResumePhysicsAfterScriptedThrow(landPosition);

        if (_binType == MailSortBinType.Confiscate)
            BeginPackageSettlement(package);
        else
            SortMailTask.Instance?.EvaluateSort(package, _binType);
    }
}

/// <summary>
/// Small forwarding component for a child trigger volume that is not on the same GameObject as
/// its owning <see cref="MailSortBin"/>. Unity never bubbles physics trigger callbacks up to
/// parent GameObjects, so without this relay, a <see cref="MailSortBin"/> whose
/// <c>_triggerZone</c> is assigned to a child Collider (e.g. a dedicated "Trigger Zone" object)
/// would never actually detect packages being dropped in.
///
/// Setup: add this component to the same GameObject as the trigger Collider (the child assigned
/// to <see cref="MailSortBin"/>'s <c>_triggerZone</c> field).
/// </summary>
public class MailSortBinTriggerRelay : MonoBehaviour
{
    private MailSortBin _bin;

    private void Awake()
    {
        _bin = GetComponentInParent<MailSortBin>();
        if (_bin == null)
            Debug.LogWarning($"[MailSortBinTriggerRelay] '{name}' has no MailSortBin in its parent hierarchy.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        _bin?.HandlePackageTriggerEnter(other);
    }
}

