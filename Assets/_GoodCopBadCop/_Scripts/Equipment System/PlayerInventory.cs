using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Two-slot hotbar inventory for the local player.
/// Press 1/2 to equip the item in that slot, or scroll the mouse wheel to cycle through
/// held/carried items (including an empty "stowed" state). Picking up an item fills the
/// first free slot automatically; placing/dropping removes it.
///
/// When both slots are occupied, pressing the other slot's key (or scrolling to it) stows
/// the held item to <see cref="stowPoint"/> (hidden on body) and brings the stored item to
/// hand. Scrolling past the last/first item cycles to the empty-hands state, which stows
/// whatever is currently held.
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

    /// <summary>
    /// The currently held item when that item is flagged <c>canBeStowed == false</c> (e.g. the
    /// supply box). Such items are deliberately NOT tracked in <see cref="_slots"/> — they occupy
    /// no hotbar slot and never appear in the HUD — but while one is carried the whole hotbar is
    /// locked (see <see cref="IsCarryingUnstowable"/>): the player must place or drop it to free
    /// their hands.
    /// </summary>
    private PickableObject _unstowableHeld;

    // ── Public surface ────────────────────────────────────────────────────────

    public int ActiveSlot => _activeSlot;

    /// <summary>
    /// True while a non-stowable item is in hand. Hotbar keys, scroll cycling and slot swapping
    /// are all disabled in this state, because honouring them would require stowing the item.
    /// </summary>
    public bool IsCarryingUnstowable => _unstowableHeld != null;

    /// <summary>Non-stowable items are carried outside the hotbar entirely.</summary>
    private static bool IsStowable(PickableObject obj) =>
        obj == null || obj.ItemData == null || obj.ItemData.canBeStowed;

    /// <summary>Fired when a slot's item reference changes. <c>item</c> is <c>null</c> when cleared.</summary>
    public event System.Action<int, PickableObject> OnSlotChanged;

    /// <summary>Fired when the active (equipped) slot index changes. -1 means no item held.</summary>
    public event System.Action<int> OnActiveSlotChanged;

    /// <summary>Returns the item in the given slot, or <c>null</c> if empty.</summary>
    public PickableObject GetItemInSlot(int index) =>
        (index >= 0 && index < _slots.Length) ? _slots[index] : null;

    /// <summary>
    /// True if both hotbar slots are occupied and <paramref name="obj"/> isn't already tracked
    /// in one of them (i.e. picking it up would need a free slot that doesn't exist). Used by
    /// <see cref="PlayerPickupController.PickUpObject"/> to block new pickups when full, while
    /// still allowing re-equipping an already-owned stowed item (e.g. via
    /// <see cref="PlayerPickupController.UnstowItemToHand"/>).
    /// Non-stowable items never consume a slot, so a full inventory never blocks them.
    /// </summary>
    public bool IsFullFor(PickableObject obj) =>
        IsStowable(obj) && SlotOf(obj) < 0 && FreeSlot() < 0;

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
        if (Input.GetKeyDown(KeyCode.R)) TryReloadActiveWeapon();

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) CycleActiveItem(1);
        else if (scroll < 0f) CycleActiveItem(-1);
    }

    /// <summary>
    /// Scroll-wheel item cycling. Builds the set of reachable states — empty hands plus any
    /// occupied slot — and steps one entry forward/backward from whichever is currently active,
    /// wrapping around. Moving onto the empty-hands state stows the held item; moving onto a
    /// slot equips/swaps to it via <see cref="EquipSlot"/> (which already knows how to swap when
    /// both slots are full).
    /// </summary>
    /// <param name="direction">+1 to scroll to the next item, -1 for the previous.</param>
    private void CycleActiveItem(int direction)
    {
        if (!IsOwner) return;

        // A non-stowable item in hand locks the hotbar — the player has to put it down.
        if (IsCarryingUnstowable) return;

        // -1 represents the empty-hands state and is always a valid destination.
        var states = new System.Collections.Generic.List<int> { -1 };
        for (int i = 0; i < _slots.Length; i++)
            if (_slots[i] != null) states.Add(i);

        if (states.Count <= 1) return; // nothing to cycle to

        int currentIndex = states.IndexOf(_activeSlot);
        if (currentIndex < 0) currentIndex = 0;

        int nextIndex = ((currentIndex + direction) % states.Count + states.Count) % states.Count;
        int nextState = states[nextIndex];

        if (nextState == -1)
        {
            if (_activeSlot >= 0) EquipSlot(_activeSlot); // pressing the active slot's key stows it
        }
        else
        {
            EquipSlot(nextState);
        }
    }

    // ── Reloading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pressing R reloads the currently equipped weapon directly from a compatible ammo item
    /// sitting in the other inventory slot, without needing to bring the ammo to hand first.
    /// No-ops if the held item isn't reloadable or no compatible ammo is carried.
    /// </summary>
    private void TryReloadActiveWeapon()
    {
        if (_activeSlot < 0) return;

        PickableObject held = _slots[_activeSlot];
        if (held is not IInventoryReloadable weapon) return;

        int otherSlot = _activeSlot == 0 ? 1 : 0;
        PickableObject ammoItem = _slots[otherSlot];
        if (ammoItem == null || !weapon.IsCompatibleAmmo(ammoItem)) return;

        weapon.ReloadFromInventory(ammoItem);
    }

    /// <summary>
    /// Removes <paramref name="item"/> from whichever slot currently holds it, if any, and fires
    /// <see cref="OnSlotChanged"/> so the HUD clears immediately. Called by weapons (e.g.
    /// <see cref="Pistol"/>, <see cref="Shotgun"/>) right before despawning an ammo item that was
    /// fully consumed via <see cref="TryReloadActiveWeapon"/> while sitting in inventory (not held).
    /// </summary>
    public void ClearSlotForItem(PickableObject item)
    {
        int index = SlotOf(item);
        if (index >= 0)
            ClearSlot(index);
    }

    // ── Pickup / drop tracking ────────────────────────────────────────────────

    private void HandleHeldObjectChanged(PickableObject obj)
    {
        if (obj != null)
        {
            // Non-stowable items bypass the hotbar entirely: no slot is consumed, no HUD icon is
            // shown, and _activeSlot stays -1 so nothing can be cycled/swapped while it is held.
            if (!IsStowable(obj))
            {
                _unstowableHeld = obj;
                SetActiveSlot(-1);
                return;
            }

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
            // A non-stowable item left the hand (placed or dropped) — it owns no slot, so there is
            // nothing to clear beyond releasing the hotbar lock.
            if (_unstowableHeld != null)
            {
                _unstowableHeld = null;
                return;
            }

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

        // A non-stowable item in hand locks the hotbar — equipping anything else would require
        // stowing it, which it does not support. The player must place or drop it first.
        if (IsCarryingUnstowable) return;

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
            // Clear the server-authoritative stowed flag BEFORE the local SetActive(true).
            // Without this the item stayed hidden (_isStowed == true) on the host and every
            // remote client — and since it is no longer tracked in any inventory, nothing
            // would ever unstow it again, so the item was lost for good.
            item.RequestSetStowedNetworked(false);
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
