using UnityEngine;

/// <summary>
/// Bridges <see cref="PlayerInventory"/> (on the runtime-spawned player) with the
/// two <see cref="InventorySlotUI"/> widgets in the HUD canvas.
///
/// For the local player it subscribes to <see cref="PlayerInventory.OnSlotChanged"/> and
/// <see cref="PlayerInventory.OnActiveSlotChanged"/>. While spectating, those events are
/// owner-only, so it instead polls the watched teammate's replicated slot mirrors
/// (<see cref="PlayerInventory.GetReplicatedItemInSlot"/> / <see cref="PlayerInventory.ReplicatedActiveSlot"/>).
/// </summary>
public class InventoryHUDController : MonoBehaviour
{
    [SerializeField] private InventorySlotUI[] slotUIs = new InventorySlotUI[2];

    private PlayerInventory _inventory;
    private bool _mirroring;

    // Last values pushed to the slot widgets in mirroring mode, so they're only touched on change.
    private PickableObject[] _mirroredItems;
    private int _mirroredActive = int.MinValue;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()  => Rebind();
    private void OnDisable() => Unsubscribe();

    private void Update()
    {
        bool mirroring = SpectateManager.IsHudMirroringTarget;
        if (mirroring != _mirroring || ResolveInventory() != _inventory)
            Rebind();

        if (_mirroring)
            RefreshMirrored();
    }

    // ── Subscription ──────────────────────────────────────────────────────────

    private static PlayerInventory ResolveInventory()
    {
        PlayerInstance subject = SpectateManager.HudSubject;
        return subject != null ? subject.GetComponent<PlayerInventory>() : null;
    }

    private void Rebind()
    {
        Unsubscribe();

        _mirroring = SpectateManager.IsHudMirroringTarget;
        _inventory = ResolveInventory();

        if (_inventory == null)
        {
            ClearSlots();
            return;
        }

        if (_mirroring)
        {
            _mirroredItems = new PickableObject[slotUIs.Length];
            _mirroredActive = int.MinValue;
            RefreshMirrored(force: true);
            return;
        }

        _inventory.OnSlotChanged       += HandleSlotChanged;
        _inventory.OnActiveSlotChanged += HandleActiveSlotChanged;

        // Sync current state immediately.
        RefreshAll();
    }

    private void Unsubscribe()
    {
        if (_inventory != null && !_mirroring)
        {
            _inventory.OnSlotChanged       -= HandleSlotChanged;
            _inventory.OnActiveSlotChanged -= HandleActiveSlotChanged;
        }

        _inventory = null;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void HandleSlotChanged(int slotIndex, PickableObject obj)
    {
        if (slotIndex < 0 || slotIndex >= slotUIs.Length) return;
        slotUIs[slotIndex].SetItem(obj);
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
            slotUIs[i].SetItem(item);
            slotUIs[i].SetSelected(_inventory.ActiveSlot == i);
        }
    }

    private void RefreshMirrored(bool force = false)
    {
        if (_inventory == null || _mirroredItems == null) return;

        for (int i = 0; i < slotUIs.Length; i++)
        {
            PickableObject item = _inventory.GetReplicatedItemInSlot(i);
            if (force || item != _mirroredItems[i])
            {
                _mirroredItems[i] = item;
                slotUIs[i].SetItem(item);
            }
        }

        int active = _inventory.ReplicatedActiveSlot;
        if (force || active != _mirroredActive)
        {
            _mirroredActive = active;
            HandleActiveSlotChanged(active);
        }
    }

    private void ClearSlots()
    {
        for (int i = 0; i < slotUIs.Length; i++)
        {
            if (slotUIs[i] == null) continue;
            slotUIs[i].SetItem(null);
            slotUIs[i].SetSelected(false);
        }
    }
}
