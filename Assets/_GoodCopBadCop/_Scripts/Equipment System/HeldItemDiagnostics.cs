using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Diagnostics for items vanishing from the local player's hand/hotbar. Logs an error with the
/// full call stack when an item the local player is holding (or has in a hotbar slot) is
/// deactivated or despawned by anything other than the normal stow path, so the system
/// responsible can be identified from a single repro log.
/// </summary>
public static class HeldItemDiagnostics
{
    private static bool s_quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        s_quitting = false;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    private static void OnQuitting() => s_quitting = true;

    /// <summary>Logs if <paramref name="item"/> is currently in the local player's hand or hotbar.</summary>
    public static void ReportIfOwnedByLocalPlayer(PickableObject item, string what)
    {
        if (s_quitting || item == null) return;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.ShutdownInProgress) return;

        PlayerInstance player = PlayerInstance.Instance;
        PlayerPickupController pickup = player != null ? player.PlayerPickupController : null;
        if (pickup == null || pickup.IsStowingHeldItem || pickup.IsHidingHeldItemForGuidebook) return;

        PlayerInventory inventory = player.GetComponent<PlayerInventory>();
        bool inHand = pickup.HeldObject == item;
        int slot = -1;
        if (inventory != null)
        {
            if (inventory.GetItemInSlot(0) == item) slot = 0;
            else if (inventory.GetItemInSlot(1) == item) slot = 1;
        }

        if (!inHand && slot < 0) return;

        Debug.LogError($"[HeldItemDiagnostics] '{item.name}' was {what} while owned by the local player " +
                       $"(inHand={inHand}, slot={(slot >= 0 ? (slot + 1).ToString() : "none")}, host={nm.IsHost}). " +
                       $"Caller:\n{System.Environment.StackTrace}", item);
    }
}

/// <summary>
/// Attached at runtime to an item when it is picked up. Reports a deactivation that happens while
/// the item is still in the local player's hand/hotbar (see <see cref="HeldItemDiagnostics"/>).
/// OnDisable runs synchronously inside SetActive(false), so the logged stack names the caller.
/// </summary>
[DisallowMultipleComponent]
public class HeldItemDisableProbe : MonoBehaviour
{
    private PickableObject _item;

    private void Awake() => _item = GetComponent<PickableObject>();

    private void OnDisable() => HeldItemDiagnostics.ReportIfOwnedByLocalPlayer(_item, "deactivated");
}
