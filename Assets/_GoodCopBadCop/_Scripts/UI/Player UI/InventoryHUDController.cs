using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bridges <see cref="PlayerInventory"/> (on the runtime-spawned player) with the
/// <see cref="InventorySlotUI"/> widgets in the HUD canvas. The prefab ships with two widgets;
/// when <see cref="GameSettings.ThreeInventorySlots"/> is on (or the bound inventory reports more
/// slots), extra widgets are cloned from the last one at runtime (see <see cref="EnsureSlotWidgets"/>).
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

    private void Awake() => EnsureSlotWidgets(GameSettings.Instance.InventorySlotCount);

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

        EnsureSlotWidgets(_inventory.SlotCount);

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

    /// <summary>
    /// Grows <see cref="slotUIs"/> to <paramref name="count"/> widgets by cloning the last one,
    /// placing each clone right after it in the panel's layout group, labelling it with its
    /// hotkey number, and widening the panel so the layout doesn't overflow. Never shrinks.
    /// </summary>
    private void EnsureSlotWidgets(int count)
    {
        if (slotUIs == null || slotUIs.Length == 0 || count <= slotUIs.Length) return;

        InventorySlotUI template = slotUIs[slotUIs.Length - 1];
        if (template == null)
        {
            Debug.LogWarning("[InventoryHUDController] Can't add HUD slots: the last slot widget is unassigned.");
            return;
        }

        var rectParent  = template.transform.parent as RectTransform;
        var layout      = rectParent != null ? rectParent.GetComponent<HorizontalLayoutGroup>() : null;
        var templateRect = template.transform as RectTransform;
        float widthStep = (templateRect != null ? templateRect.rect.width : 0f) + (layout != null ? layout.spacing : 0f);

        int oldLength = slotUIs.Length;
        System.Array.Resize(ref slotUIs, count);

        for (int i = oldLength; i < count; i++)
        {
            InventorySlotUI previous = slotUIs[i - 1];
            InventorySlotUI clone = Instantiate(template, template.transform.parent);
            clone.name = $"Inventory Slot ({i + 1})";
            clone.transform.SetSiblingIndex(previous.transform.GetSiblingIndex() + 1);
            clone.SetHotkeyLabel((i + 1).ToString());
            clone.SetItem(null);
            clone.SetSelected(false);
            slotUIs[i] = clone;

            if (rectParent != null && layout != null)
                rectParent.sizeDelta += new Vector2(widthStep, 0f);
        }

        // Keep the mirroring cache in step with the widget count.
        if (_mirroredItems != null && _mirroredItems.Length != slotUIs.Length)
            System.Array.Resize(ref _mirroredItems, slotUIs.Length);
    }

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
