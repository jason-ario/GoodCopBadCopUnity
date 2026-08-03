using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Place this component on a trigger-collider GameObject inside (or near) the area that
/// should activate a <see cref="MutantSpawner"/> when players enter it.
///
/// The linked spawner must have <c>Requires Zone Activation</c> checked. When the first
/// player enters this collider, <see cref="MutantSpawner.ActivateFromZone"/> is called on
/// the server to begin the spawn loop (provided the day threshold is met — ambient spawning
/// runs any time of day, it is not gated by shift/day-night state).
///
/// Optionally, when <see cref="_deactivateWhenAllPlayersLeave"/> is enabled, the spawner
/// pauses again once every player has exited and will re-arm itself for the next entry.
///
/// Reusable — add to any trigger volume and point it at any <see cref="MutantSpawner"/>.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MutantSpawnerZoneTrigger : MonoBehaviour
{
    [Tooltip("The MutantSpawner to activate when a player enters this zone. " +
             "Must have 'Requires Zone Activation' enabled on the spawner.")]
    [SerializeField] private MutantSpawner _spawner;

    [Tooltip("When enabled, the spawner pauses again once the last player leaves this zone. " +
             "The zone re-arms itself for the next entry. " +
             "When disabled (default), the spawner keeps running indefinitely once activated, " +
             "regardless of zone occupancy or time of day.")]
    [SerializeField] private bool _deactivateWhenAllPlayersLeave = false;

    // How many player objects are currently inside the trigger volume.
    private int _playersInZone;

    private void Awake()
    {
        // Ensure the collider on this GameObject is always a trigger.
        Collider col = GetComponent<Collider>();
        if (!col.isTrigger)
        {
            col.isTrigger = true;
            Debug.LogWarning($"[MutantSpawnerZoneTrigger] Collider on '{name}' was not a trigger — forced to trigger.", this);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer()) return;
        if (!IsPlayer(other)) return;

        _playersInZone++;
        Debug.Log($"[MutantSpawnerZoneTrigger] Player entered zone '{name}'. Players in zone: {_playersInZone}.", this);

        if (_playersInZone == 1)
            _spawner?.ActivateFromZone();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer()) return;
        if (!IsPlayer(other)) return;

        _playersInZone = Mathf.Max(0, _playersInZone - 1);
        Debug.Log($"[MutantSpawnerZoneTrigger] Player left zone '{name}'. Players in zone: {_playersInZone}.", this);

        if (_playersInZone == 0 && _deactivateWhenAllPlayersLeave)
            _spawner?.DeactivateFromZone();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Returns true when running on the server/host.</summary>
    private static bool IsServer() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    /// <summary>
    /// Returns true if the collider (or any of its parents) belongs to a player,
    /// identified by the presence of a <see cref="PlayerMovementController"/> component.
    /// </summary>
    private static bool IsPlayer(Collider other) =>
        other.GetComponentInParent<PlayerMovementController>() != null;

    // ── Gizmos ─────────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        DrawColliderGizmo(col);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.7f);
        DrawColliderWireGizmo(col);
    }

    private void OnDrawGizmosSelected()
    {
        Collider col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
        DrawColliderGizmo(col);

        Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
        DrawColliderWireGizmo(col);

        // Draw a line to the linked spawner for easy visual debugging.
        if (_spawner != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.DrawLine(transform.position, _spawner.transform.position);
        }
    }

    private static void DrawColliderGizmo(Collider col)
    {
        Gizmos.matrix = col.transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawCube(box.center, box.size);
        else if (col is SphereCollider sphere)
            Gizmos.DrawSphere(sphere.center, sphere.radius);
    }

    private static void DrawColliderWireGizmo(Collider col)
    {
        Gizmos.matrix = col.transform.localToWorldMatrix;
        if (col is BoxCollider box)
            Gizmos.DrawWireCube(box.center, box.size);
        else if (col is SphereCollider sphere)
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
    }
}
