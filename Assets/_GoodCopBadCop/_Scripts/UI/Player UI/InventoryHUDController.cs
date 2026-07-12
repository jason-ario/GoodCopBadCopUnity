using UnityEngine;

/// <summary>
/// Bridges <see cref="PlayerInventory"/> (on the runtime-spawned player) with the
/// two <see cref="InventorySlotUI"/> widgets in the HUD canvas.
///
/// Subscribes to <see cref="PlayerInventory.OnSlotChanged"/> and
/// <see cref="PlayerInventory.OnActiveSlotChanged"/> to keep the UI in sync.
/// Uses the same lazy-subscribe pattern as <see cref="HealthBar"/> and
/// <see cref="RadiationBarUI"/> so it works even when the player spawns after the scene.
/// </summary>
public class InventoryHUDController : MonoBehaviour
{
    [SerializeField] private InventorySlotUI[] slotUIs = new InventorySlotUI[2];

    private PlayerInventory _inventory;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()  => TrySubscribe();
    private void OnDisable() => Unsubscribe();

    private void Update()
    {
        if (_inventory == null) TrySubscribe();
    }

    // ── Subscription ──────────────────────────────────────────────────────────

    private void TrySubscribe()
    {
        if (PlayerInstance.Instance == null) return;

        PlayerInventory inv = PlayerInstance.Instance.GetComponent<PlayerInventory>();
        if (inv == null) return;

        _inventory = inv;
        _inventory.OnSlotChanged       += HandleSlotChanged;
        _inventory.OnActiveSlotChanged += HandleActiveSlotChanged;

        // Sync current state immediately.
        RefreshAll();
    }

    private void Unsubscribe()
    {
        if (_inventory == null) return;

        _inventory.OnSlotChanged       -= HandleSlotChanged;
        _inventory.OnActiveSlotChanged -= HandleActiveSlotChanged;
        _inventory = null;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleSlotChanged(int slotIndex, PickableObject obj)
    {
        if (slotIndex < 0 || slotIndex >= slotUIs.Length) return;
        slotUIs[slotIndex].SetItem(obj?.ItemData);
    }

    private void HandleActiveSlotChanged(int activeIndex)
    {
        for (int i = 0; i < slotUIs.Length; i++)
            slotUIs[i].SetSelected(i == activeIndex);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        if (_inventory == null) return;

        for (int i = 0; i < slotUIs.Length; i++)
        {
            PickableObject item = _inventory.GetItemInSlot(i);
            slotUIs[i].SetItem(item?.ItemData);
            slotUIs[i].SetSelected(_inventory.ActiveSlot == i);
        }
    }
}
