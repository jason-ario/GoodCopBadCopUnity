using UnityEngine;

/// <summary>
/// Trigger volume placed inside a dumpster's opening that detects <see cref="TrashBag"/>s
/// thrown at it with real physics (via ThrowController / PickableObject.ThrowServerRpc) and
/// deposits them automatically — no left-click interact required.
///
/// This is purely a local trigger listener: every client's Collider will fire
/// OnTriggerEnter as the (server-simulated, NetworkTransform-replicated) bag flies through,
/// but the actual deposit only ever runs on the server via
/// <see cref="DumpsterInteractable.TryDepositThrownBag"/>.
///
/// Prefab setup:
///   - Child of the Dumpster prefab root, positioned inside the bin opening.
///   - Collider with isTrigger = true, sized to cover the interior catch volume.
///   - No Rigidbody needed here — the TrashBag already has one.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DumpsterPhysicsDepositZone : MonoBehaviour
{
    [Tooltip("The dumpster this zone deposits into. Auto-resolved from a parent if left empty.")]
    [SerializeField] private DumpsterInteractable _dumpster;

    private void Awake()
    {
        if (_dumpster == null)
            _dumpster = GetComponentInParent<DumpsterInteractable>();

        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"[DumpsterPhysicsDepositZone] '{name}' collider is not a trigger — physics deposits will never be detected.", this);

        if (_dumpster == null)
            Debug.LogWarning($"[DumpsterPhysicsDepositZone] '{name}' could not find a DumpsterInteractable in its parents.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_dumpster == null) return;

        TrashBag bag = other.GetComponentInParent<TrashBag>();
        if (bag == null) return;

        _dumpster.TryDepositThrownBag(bag);
    }
}
