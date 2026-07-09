using System.Linq;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the three-slot fuse-box puzzle introduced in Day 3's "Repair the Power" task.
///
/// Subscribe to <see cref="FuseSlot.OnFuseInserted"/> on all three slots. When the server
/// detects that all slots are filled:
///   1. Fires <see cref="CompletePuzzleClientRpc"/> on every client, which resolves the
///      <see cref="RepairPowerThreat"/> in each client's <see cref="TaskRegistry"/>.
///   2. Calls <see cref="ElectricityController.PowerOn"/> (server-only) to restore power.
///
/// Setup notes:
///   - Place on the root of the Fuse Box prefab (alongside a NetworkObject).
///   - Assign the three <see cref="FuseSlot"/> child components to <see cref="_slots"/>.
///   - Assign the scene's <see cref="ElectricityController"/> to <see cref="_electricityController"/>.
///   - Optionally assign a completion sound that plays on all clients when the puzzle finishes.
/// </summary>
public class FuseBoxPuzzleController : NetworkBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Fuse Box Puzzle")]
    [Tooltip("The three FuseSlot components on this fuse-box panel, in order (slot 0, 1, 2).")]
    [SerializeField] private FuseSlot[] _slots;

    [Tooltip("The scene's ElectricityController. PowerOn() is called server-side when all slots are filled.")]
    [SerializeField] private ElectricityController _electricityController;

    [Tooltip("Optional sound played on all clients when the puzzle is completed.")]
    [SerializeField] private AudioClip _completionSound;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (_slots == null || _slots.Length == 0)
        {
            Debug.LogError("[FuseBoxPuzzleController] No FuseSlots assigned in Inspector.");
            return;
        }

        foreach (FuseSlot slot in _slots)
        {
            if (slot != null)
                slot.OnFuseInserted += OnAnySlotFilled;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_slots == null) return;

        foreach (FuseSlot slot in _slots)
        {
            if (slot != null)
                slot.OnFuseInserted -= OnAnySlotFilled;
        }
    }

    // ── Slot completion logic ─────────────────────────────────────────────────

    /// <summary>
    /// Fired on every client (via <see cref="FuseSlot.OnFuseInserted"/>) each time any slot
    /// is filled. Only the server checks whether all slots are now filled and triggers
    /// the completion sequence.
    /// </summary>
    private void OnAnySlotFilled()
    {
        if (!IsServer) return;

        bool allFilled = _slots.All(s => s != null && s.IsFilled);
        if (!allFilled) return;

        Debug.Log("[FuseBoxPuzzleController] All fuse slots filled — puzzle complete!");

        // Restore power on the server (broadcasts to clients via ElectricityController's own RPC).
        if (_electricityController != null)
            _electricityController.PowerOn();
        else
            Debug.LogWarning("[FuseBoxPuzzleController] _electricityController not assigned — power restoration skipped.");

        // Resolve the RepairPowerThreat on every client's TaskRegistry.
        CompletePuzzleClientRpc();
    }

    // ── Client RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Received on all clients when the puzzle is complete. Resolves the
    /// <see cref="RepairPowerThreat"/> in the local <see cref="TaskRegistry"/> and
    /// plays the optional completion sound.
    /// </summary>
    [ClientRpc]
    private void CompletePuzzleClientRpc()
    {
        // Each client has its own RepairPowerThreat instance added by Day_03.
        RepairPowerThreat threat = TaskRegistry.Instance?.Threats
            .OfType<RepairPowerThreat>()
            .FirstOrDefault();

        if (threat != null)
        {
            threat.Resolve();
            Debug.Log("[FuseBoxPuzzleController] RepairPowerThreat resolved on this client.");
        }
        else
        {
            Debug.LogWarning("[FuseBoxPuzzleController] No RepairPowerThreat found in TaskRegistry — was the Day 3 phone call answered?");
        }

        if (_completionSound != null)
            SFXController.Instance?.PlayAtPosition(_completionSound, transform.position);
    }
}
