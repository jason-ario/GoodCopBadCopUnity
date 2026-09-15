using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Two-slot hotbar inventory for the local player.
/// Press 1/2 to equip the item in that slot, or scroll the mouse wheel to cycle through
/// held/carried items. Picking up an item brings it straight to hand; placing/dropping empties
/// the hand.
///
/// A hotbar slot represents a STOWED item only — hands are never a "slot". While only one slot
/// is occupied, its key freely toggles that item between hand and stowed (hidden on body) via
/// <see cref="stowPoint"/>. Once BOTH slots are occupied, you can never be empty-handed: pressing
/// the currently-held item's own key is then a no-op, and pressing the other slot's key swaps —
/// stowing the held item and bringing the other one to hand in the same motion — so there is
/// always exactly one item in hand whenever two are carried.
/// Because a slot only ever holds a stowed item, an empty hand can always pick something up even
/// if both slots are already stowed — the new item is simply carried unslotted until a slot
/// frees up to stow it (see <see cref="IsHandLocked"/>).
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

    // The hotbar is managed by its owner, but the host needs the authoritative references when
    // it serializes a resumable workday. These owner-write mirrors make stowed items visible to
    // the server without making the host responsible for reconstructing client-local hotbar UI.
    private readonly NetworkVariable<NetworkObjectReference> _firstSlotRef = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<NetworkObjectReference> _secondSlotRef = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private int _activeSlot = -1;   // -1 = hand empty

    /// <summary>
    /// The currently held item when it isn't tracked by either hotbar slot. This covers two
    /// cases: the item is flagged <c>canBeStowed == false</c> (e.g. the supply box), or it IS
    /// stowable but both hotbar slots were already occupied by stowed items when it was picked
    /// up (see <see cref="HandleHeldObjectChanged"/>). Either way it occupies no hotbar slot and
    /// never appears in the HUD, and while it's carried the whole hotbar is locked (see
    /// <see cref="IsHandLocked"/>): the player must place or drop it to free their hands — a
    /// hotbar slot may then be free to stow it, or they can carry on holding a fresh pickup.
    /// </summary>
    private PickableObject _unslottedHeld;

    // ── Public surface ────────────────────────────────────────────────────────

    public int ActiveSlot => _activeSlot;

    /// <summary>
    /// True while an item that isn't tracked by any hotbar slot is in hand (see
    /// <see cref="_unslottedHeld"/>). Hotbar keys, scroll cycling and slot swapping are all
    /// disabled in this state, because honouring them would require stowing the item.
    /// </summary>
    public bool IsHandLocked => _unslottedHeld != null;

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
    /// Adds every item currently owned by this inventory to a host-side workday snapshot. The
    /// local arrays cover the host player's immediate state, while the replicated references
    /// cover remote players whose local hotbar arrays are intentionally private to their owner.
    /// </summary>
    public void AppendSaveItemIds(HashSet<string> itemIds)
    {
        if (itemIds == null) return;

        AddSaveId(_slots[0], itemIds);
        AddSaveId(_slots[1], itemIds);
        AddSaveId(_unslottedHeld, itemIds);
        AddSaveId(_firstSlotRef.Value, itemIds);
        AddSaveId(_secondSlotRef.Value, itemIds);
        if (_pickup != null)
            AddSaveId(_pickup.HeldObjectRef, itemIds);
    }

    private static void AddSaveId(PickableObject item, HashSet<string> itemIds)
    {
        if (item != null && !string.IsNullOrEmpty(item.SaveId))
            itemIds.Add(item.SaveId);
    }

    private static void AddSaveId(NetworkObjectReference itemRef, HashSet<string> itemIds)
    {
        if (!itemRef.TryGet(out NetworkObject itemObject)) return;
        AddSaveId(itemObject.GetComponent<PickableObject>(), itemIds);
    }

    private void SetSlotReference(int index, PickableObject item)
    {
        if (!IsOwner) return;

        NetworkObjectReference itemRef = item != null && item.NetworkObject != null
            ? new NetworkObjectReference(item.NetworkObject)
            : default;

        if (index == 0)
            _firstSlotRef.Value = itemRef;
        else if (index == 1)
            _secondSlotRef.Value = itemRef;
    }

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

        // A locked hotbar (unslotted item in hand) still receives legacy hotkey and wheel input.
        // Ignore it so an unseen 1/2 or wheel press cannot stow/swap an item and leave the hotbar
        // in an unexpected state when normal gameplay resumes.
        if (PlayerInstance.Instance != null &&
            (PlayerInstance.Instance.IsInCutscene ||
             ScriptedDialogueRunner.IsScriptedModeActive ||
             DialogueChoiceSystem.IsInDialogueMode))
            return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) EquipSlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) EquipSlot(1);
        if (Input.GetKeyDown(KeyCode.R)) TryReloadActiveWeapon();

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) CycleActiveItem(1);
        else if (scroll < 0f) CycleActiveItem(-1);
    }

    /// <summary>
    /// Scroll-wheel item cycling. Builds the set of reachable states — any occupied slot, plus
    /// the empty-hands state UNLESS both slots are already full (with both full, empty-hands is
    /// never reachable — see <see cref="EquipSlot"/>) — and steps one entry forward/backward from
    /// whichever is currently active, wrapping around. Moving onto the empty-hands state stows
    /// the held item; moving onto a slot equips it via <see cref="EquipSlot"/>, toggling/swapping
    /// with whatever is currently held if needed.
    /// </summary>
    /// <param name="direction">+1 to scroll to the next item, -1 for the previous.</param>
    private void CycleActiveItem(int direction)
    {
        if (!IsOwner) return;

        // An unslotted item in hand locks the hotbar — the player has to put it down.
        if (IsHandLocked) return;

        bool bothFull = _slots[0] != null && _slots[1] != null;

        var states = new System.Collections.Generic.List<int>();
        if (!bothFull) states.Add(-1); // empty-hands is unreachable once both slots are full
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
                _unslottedHeld = obj;
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
                // Both hotbar slots are already stowed. A slot only ever represents a STOWED
                // item, so a hand that's otherwise empty is always free to pick something up —
                // this item is simply carried unslotted (like a non-stowable item) until it's
                // placed/dropped or a slot frees up to stow it.
                _unslottedHeld = obj;
                SetActiveSlot(-1);
                return;
            }

            _slots[free]  = obj;
            _stowed[free] = false;
            SetSlotReference(free, obj);
            OnSlotChanged?.Invoke(free, obj);
            SetActiveSlot(free);
        }
        else
        {
            // An unslotted item left the hand (placed or dropped) — it owns no slot, so there is
            // nothing to clear beyond releasing the hotbar lock.
            if (_unslottedHeld != null)
            {
                _unslottedHeld = null;
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
    /// Pressing a slot's hotkey toggles the hotbar. If that slot's item is stowed, it's brought
    /// to hand — swapping with whatever the other slot is currently holding, if anything, so a
    /// hand is never left empty while both slots are full. If that slot's item is already held,
    /// pressing its own key stows it to an empty hand, but only when the other slot is empty:
    /// with both slots full you can never be empty-handed, so pressing the held item's own key
    /// in that case is a no-op — press the OTHER slot's key to toggle to it instead.
    /// </summary>
    public void EquipSlot(int slotIndex)
    {
        if (!IsOwner) return;
        if (slotIndex < 0 || slotIndex >= 2) return;

        // An unslotted item in hand locks the hotbar — equipping anything else would require
        // stowing it, which it does not support. The player must place or drop it first.
        if (IsHandLocked) return;

        PickableObject target = _slots[slotIndex];
        if (target == null) return;   // empty slot — nothing to do

        int otherIndex = slotIndex == 0 ? 1 : 0;
        bool otherOccupied = _slots[otherIndex] != null;

        // ── Pressing the hotkey for the already-held item ─────────────────────
        if (_activeSlot == slotIndex && !_stowed[slotIndex])
        {
            // Both slots full: stowing this would leave both hands empty. No-op — the other
            // slot's key is how you toggle away from this item.
            if (otherOccupied) return;

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

        // ── Pressing the hotkey for a stowed slot → bring it to hand ──────────
        if (_stowed[slotIndex])
        {
            if (_pickup.HeldObject == null)
            {
                _pickup.UnstowItemToHand(target);
                // _stowed and _activeSlot updated in HandleHeldObjectChanged.
                return;
            }

            // Hand busy with the other slot's item — toggle: stow it, then bring this one up,
            // so a hand is never left empty in between.
            if (stowPoint == null)
            {
                Debug.LogWarning("[PlayerInventory] stowPoint is not assigned — slot toggle disabled.");
                return;
            }

            int currentSlot = _activeSlot;
            PickableObject stowedItem = _pickup.StowCurrentItemToPoint(stowPoint);
            if (stowedItem != null)
                _stowed[currentSlot] = true;

            _pickup.UnstowItemToHand(target);
            // _stowed[slotIndex] = false and SetActiveSlot handled in HandleHeldObjectChanged.
        }
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
        SetSlotReference(index, null);
        OnSlotChanged?.Invoke(index, null);
    }

    /// <summary>
    /// Re-activates a stowed item, drops it back to the world, and clears its slot.
    /// Called when this component despawns (disconnect) to avoid losing stowed items.
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
