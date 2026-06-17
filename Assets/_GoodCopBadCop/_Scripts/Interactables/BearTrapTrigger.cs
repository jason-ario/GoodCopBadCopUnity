using UnityEngine;

/// <summary>
/// Placed on a bear trap's trigger child collider. Filters entering/exiting
/// colliders to the expected victim type and forwards the events to the parent
/// <see cref="BearTrap"/>.
/// </summary>
public class BearTrapTrigger : MonoBehaviour
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
        ? other.GetComponentInParent<PlayerMovementController>() != null
        : other.GetComponentInParent<MutantEnemy>() != null;
}
