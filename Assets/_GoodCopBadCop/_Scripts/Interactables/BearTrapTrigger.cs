using UnityEngine;

/// <summary>
/// Placed on a bear trap's trigger child collider. Filters entering/exiting
/// colliders to the expected victim type and forwards the events to the parent
/// <see cref="BearTrap"/>.
/// Implements <see cref="ISelfManagedCollider"/>: the zone must stay a trigger and its enabled
/// state is driven solely by <see cref="BearTrap"/>'s armed state, never by the pickup's
/// held/released collider handling.
/// </summary>
public class BearTrapTrigger : MonoBehaviour, ISelfManagedCollider
{
    [Tooltip("When true this zone only reacts to players; when false, only to MutantEnemy.")]
    [SerializeField] private bool _isPlayerTrigger;

    private BearTrap _bearTrap;

    private void Awake()
    {
        _bearTrap = GetComponentInParent<BearTrap>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsRelevant(other)) return;
        _bearTrap?.OnTriggerZoneEntered(other, _isPlayerTrigger);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsRelevant(other)) return;
        _bearTrap?.OnTriggerZoneExited(other, _isPlayerTrigger);
    }

    /// <summary>
    /// Returns true when the collider belongs to the victim type this zone is
    /// configured to catch (player or enemy).
    /// </summary>
    private bool IsRelevant(Collider other) => _isPlayerTrigger
        ? BearTrap.IsPlayerVictim(other, out _)
        : BearTrap.IsEnemyVictim(other, out _);
}
