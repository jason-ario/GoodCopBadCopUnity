using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Two-slot hotbar inventory for the local player.
/// Press 1/2 to equip the item in that slot.  Picking up an item fills the first free
/// slot automatically; placing/dropping removes it.
///
/// When both slots are occupied, pressing the other slot's key stows the held item to
/// <see cref="stowPoint"/> (hidden on body) and brings the stored item to hand.
/// Requires <see cref="PlayerPickupController"/> on the same GameObject.
/// </summary>
[RequireComponent(typeof(PlayerPickupController))]
public class PlayerInventory : NetworkBehaviour
{
    [Tooltip("Child Transform on the player used as the physical anchor for a stashed item. " +
             "Position it at the hip or belt. If unassigned, swapping between full slots is disabled.")]
    [SerializeField] private Transform stowPoint;

    private PlayerPickupController _pickup;

    private readonly PickableObject[] _slots  = new PickableObject[2];
    private readonly bool[]           _stowed = new bool[2];   // true = item at stowPoint, not in hand

    private int _activeSlot = -1;   // -1 = hand empty

    // ── Public surface ────────────────────────────────────────────────────────

    public int ActiveSlot => _activeSlot;

    /// <summary>Fired when a slot's item reference changes. <c>item</c> is <c>null</c> when cleared.</summary>
    public event System.Action<int, PickableObject> OnSlotChanged;

    /// <summary>Fired when the active (equipped) slot index changes. -1 means no item held.</summary>
    public event System.Action<int> OnActiveSlotChanged;

    /// <summary>Returns the item in the given slot, or <c>null</c> if empty.</summary>
    public PickableObject GetItemInSlot(int index) =>
        (index >= 0 && index < _slots.Length) ? _slots[index] : null;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake() => _pickup = GetComponent<PlayerPickupController>();

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsOwner) return;

        _pickup.OnHeldObjectChanged += HandleHeldObjectChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (_pickup != null)
            _pickup.OnHeldObjectChanged -= HandleHeldObjectChanged;

        // Drop any stowed items back to the world so they are not lost on disconnect.
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] != null && _stowed[i])
                EvictStowedItem(i);
        }
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!IsOwner) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) EquipSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) EquipSlot(1);
    }

    // ── Pickup / drop tracking ────────────────────────────────────────────────

    private void HandleHeldObjectChanged(PickableObject obj)
    {
        if (obj != null)
        {
            // Already tracked — this fires when UnstowItemToHand calls PickUpObject.
            int existing = SlotOf(obj);
            if (existing >= 0)
            {
                _stowed[existing] = false;
                SetActiveSlot(existing);
                return;
            }

            // Brand-new item — fill first free slot.
            int free = FreeSlot();
            if (free < 0)
            {
                // Both slots occupied: evict the inactive stowed slot to make room.
                int inactive = _activeSlot == 0 ? 1 : 0;
                EvictStowedItem(inactive);
                free = inactive;
            }

            _slots[free]  = obj;
            _stowed[free] = false;
            OnSlotChanged?.Invoke(free, obj);
            SetActiveSlot(free);
        }
        else
        {
            // Hand became empty via normal drop or place (not a stow operation).
            if (_activeSlot >= 0 && !_stowed[_activeSlot])
            {
                ClearSlot(_activeSlot);
                SetActiveSlot(-1);
            }
        }
    }

    // ── Slot equipping ────────────────────────────────────────────────────────

    /// <summary>
    /// Pressing the hotkey for the active slot stows it away.
    /// Pressing the hotkey for an inactive slot brings it to hand (swapping if needed).
    /// </summary>
    public void EquipSlot(int slotIndex)
    {
        if (!IsOwner) return;
        if (slotIndex < 0 || slotIndex >= 2) return;

        PickableObject target = _slots[slotIndex];
        if (target == null) return;   // empty slot — nothing to do

        // ── Pressing the hotkey for the already-held item → stow it ──────────
        if (_activeSlot == slotIndex && !_stowed[slotIndex])
        {
            if (stowPoint == null)
            {
                Debug.LogWarning("[PlayerInventory] stowPoint is not assigned — cannot stow.");
                return;
            }

            PickableObject stowed = _pickup.StowCurrentItemToPoint(stowPoint);
            if (stowed != null)
            {
                _stowed[slotIndex] = true;
                SetActiveSlot(-1);
            }
            return;
        }

        // ── Pressing the hotkey for a stowed / inactive slot → equip it ──────
        if (_pickup.HeldObject == null)
        {
            if (_stowed[slotIndex])
            {
                _pickup.UnstowItemToHand(target);
                // _stowed and _activeSlot updated in HandleHeldObjectChanged.
            }
            return;
        }

        // ── Both slots occupied: swap ─────────────────────────────────────────
        if (stowPoint == null)
        {
            Debug.LogWarning("[PlayerInventory] stowPoint is not assigned — slot swap disabled.");
            return;
        }

        int currentSlot = _activeSlot;
        PickableObject stowedItem = _pickup.StowCurrentItemToPoint(stowPoint);
        if (stowedItem != null)
            _stowed[currentSlot] = true;

        _pickup.UnstowItemToHand(target);
        // _stowed[slotIndex] = false and SetActiveSlot handled in HandleHeldObjectChanged.
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void SetActiveSlot(int index)
    {
        _activeSlot = index;
        OnActiveSlotChanged?.Invoke(index);
    }

    private void ClearSlot(int index)
    {
        _slots[index]  = null;
        _stowed[index] = false;
        OnSlotChanged?.Invoke(index, null);
    }

    /// <summary>
    /// Re-activates a stowed item, drops it back to the world, and clears its slot.
    /// Called when both slots are full and a new item is picked up.
    /// </summary>
    private void EvictStowedItem(int index)
    {
        PickableObject item = _slots[index];
        if (item == null) return;

        if (_stowed[index])
        {
            item.gameObject.SetActive(true);
            item.RemoveParent();
            item.ReleaseHolderServerRpc();
            item.DropServerRpc(item.transform.position, item.transform.rotation);
            item.OnDropped();
        }

        ClearSlot(index);
    }

    private int SlotOf(PickableObject obj)
    {
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] == obj) return i;
        return -1;
    }

    private int FreeSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] == null) return i;
        return -1;
    }
}
