using System.Collections.Generic;
using GoodCopBadCop.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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
    /// Slot contents as seen by any peer, resolved from the owner-written slot mirrors. Unlike
    /// <see cref="GetItemInSlot"/> (owner-only local state), this works on observers — used by the
    /// hotbar HUD while spectating this player.
    /// </summary>
    public PickableObject GetReplicatedItemInSlot(int index)
    {
        NetworkObjectReference itemRef = index == 0 ? _firstSlotRef.Value
                                       : index == 1 ? _secondSlotRef.Value
                                       : default;
        return itemRef.TryGet(out NetworkObject itemObject) ? itemObject.GetComponent<PickableObject>() : null;
    }

    /// <summary>
    /// Active slot as seen by any peer: the slot whose mirrored item is the replicated held
    /// object, or -1 when the hand is empty or holds an unslotted item.
    /// </summary>
    public int ReplicatedActiveSlot
    {
        get
        {
            if (_pickup == null) return -1;
            ulong heldId = _pickup.HeldObjectRef.NetworkObjectId;
            if (heldId == 0) return -1;
            if (_firstSlotRef.Value.NetworkObjectId == heldId) return 0;
            if (_secondSlotRef.Value.NetworkObjectId == heldId) return 1;
            return -1;
        }
    }

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

    private void Awake()
    {
        _pickup = GetComponent<PlayerPickupController>();
        _health = GetComponent<PlayerHealth>();
        _animator = GetComponent<Animator>();
    }

    private PlayerHealth _health;
    private Animator _animator;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && _health != null)
            _health.OnDeath += DropAllItemsOnDeathServer;

        if (!IsOwner) return;

        _pickup.OnHeldObjectChanged += HandleHeldObjectChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (_pickup != null)
            _pickup.OnHeldObjectChanged -= HandleHeldObjectChanged;

        if (_health != null)
            _health.OnDeath -= DropAllItemsOnDeathServer;

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

        // Runs before every input gate (cutscenes, end-of-shift report, pause) so the hotbar can
        // never stay desynced from the real hand while control is locked.
        ReconcileSlotState();

        // A locked hotbar (unslotted item in hand) still receives legacy hotkey and wheel input.
        // Ignore it so an unseen 1/2 or wheel press cannot stow/swap an item and leave the hotbar
        // in an unexpected state when normal gameplay resumes.
        if (PlayerInstance.Instance != null &&
            (PlayerInstance.Instance.IsInCutscene ||
             ScriptedDialogueRunner.IsScriptedModeActive ||
             DialogueChoiceSystem.IsInDialogueMode))
            return;

        // No hotbar input while the player can't move/interact — modal screens (end-of-shift
        // report, diegetic views, PC, popups) disable control but the legacy wheel/hotkeys
        // would otherwise still swap items behind them.
        if (PlayerInstance.Instance != null &&
            (!PlayerInstance.Instance.CanControl || PlayerInstance.Instance.IsViewingShiftReport))
            return;

        if (UIController.Instance != null && UIController.Instance.IsPaused)
            return;

        if (!TextInputFocus.IsCapturingKeyboard)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) EquipSlot(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) EquipSlot(1);
        }
        if (ReloadPressed()) TryReloadActiveWeapon();

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
    /// <see cref="GameAction.Reload"/> (R by default, rebindable in Settings -> Controls) or the fixed
    /// gamepad button from <see cref="RebindableInput.GamepadButtonName"/> (Y / buttonNorth).
    /// Ignored while paused so the gamepad button can't fire through the pause menu.
    /// </summary>
    private static bool ReloadPressed()
    {
        if (UIController.Instance != null && UIController.Instance.IsPaused) return false;
        if (RebindableInput.GetKeyDown(GameAction.Reload)) return true;
        return Gamepad.current?.buttonNorth.wasPressedThisFrame ?? false;
    }

    /// <summary>
    /// Reloads the weapon currently in hand from the local player's <see cref="PlayerAmmoReserve"/>.
    /// Uses the held object rather than the active hotbar slot so an unslotted weapon (picked up
    /// while both slots were full) can still be reloaded. No-ops if the held item isn't reloadable.
    /// </summary>
    private void TryReloadActiveWeapon()
    {
        if (_pickup != null && _pickup.HeldObject is IInventoryReloadable weapon)
            weapon.RequestReloadFromReserve();
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

        // Self-heal a stale hand lock: an empty hand can never be "holding" an unslotted item.
        // Without this, a lock left behind by any release path that skipped the inventory
        // refused every hotbar press until a fresh pickup happened to reset it.
        if (IsHandLocked && _pickup.HeldObject == null)
        {
            Debug.LogWarning($"[PlayerInventory] Cleared stale hand lock ('{(_unslottedHeld != null ? _unslottedHeld.name : "destroyed")}') on an empty hand while equipping slot {slotIndex + 1}.");
            _unslottedHeld = null;
        }

        // An unslotted item in hand locks the hotbar — equipping anything else would require
        // stowing it, which it does not support. The player must place or drop it first.
        if (IsHandLocked) return;

        PickableObject target = _slots[slotIndex];
        if (target == null) return;   // empty slot — nothing to do

        // Stale "in hand" flag on a slot whose item isn't actually in the (empty) hand: treat it
        // as stowed so the press brings it back instead of trying to stow an empty hand.
        if (_activeSlot == slotIndex && !_stowed[slotIndex] && _pickup.HeldObject == null)
        {
            Debug.LogWarning($"[PlayerInventory] Slot {slotIndex + 1} '{target.name}' was flagged in-hand with an empty hand — re-equipping it.");
            _stowed[slotIndex] = true;
            SetActiveSlot(-1);
        }

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
                WarnIfUnstowFailed(slotIndex, target);
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
            if (currentSlot < 0 || currentSlot == slotIndex)
            {
                // Hand holds something the hotbar doesn't own as the active slot — stowing it
                // would orphan it. Refuse loudly instead of indexing _stowed[-1].
                Debug.LogWarning($"[PlayerInventory] Can't equip slot {slotIndex + 1}: hand holds '{_pickup.HeldObject.name}' which isn't the active slot item (activeSlot={_activeSlot}).");
                return;
            }

            PickableObject stowedItem = _pickup.StowCurrentItemToPoint(stowPoint);
            if (stowedItem == null) return;   // couldn't free the hand — leave both items as they were
            _stowed[currentSlot] = true;
            SetActiveSlot(-1);

            _pickup.UnstowItemToHand(target);
            // _stowed[slotIndex] = false and SetActiveSlot handled in HandleHeldObjectChanged.
            WarnIfUnstowFailed(slotIndex, target);
        }
    }

    /// <summary>
    /// <see cref="PlayerPickupController.UnstowItemToHand"/> can bail out silently (e.g. the item
    /// reads as held by another client). Log the state so a "slot won't equip" repro names the
    /// cause instead of looking like a dead key.
    /// </summary>
    private void WarnIfUnstowFailed(int slotIndex, PickableObject target)
    {
        if (target == null || _pickup.HeldObject == target) return;

        Debug.LogWarning($"[PlayerInventory] Slot {slotIndex + 1} '{target.name}' failed to come to hand " +
                         $"(held='{(_pickup.HeldObject != null ? _pickup.HeldObject.name : "nothing")}', " +
                         $"holder={target.HolderClientId}, local={NetworkManager.Singleton.LocalClientId}, " +
                         $"active={target.gameObject.activeSelf}, stowed={_stowed[slotIndex]}).");
    }

    // ── Death drop ────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-only <see cref="PlayerHealth.OnDeath"/> handler. Drops the item in hand and every
    /// stowed hotbar item into the world with real physics and no throw force, so nothing stays
    /// hovering where the player died. Uses the replicated held/slot references, so it is
    /// server-authoritative and works whether or not the dead client's own teardown lands.
    /// The owner separately clears its local hold/hotbar state (<see cref="ClearAllSlotsLocalOnDeath"/>).
    /// </summary>
    private void DropAllItemsOnDeathServer()
    {
        if (!IsServer) return;

        ulong ownerId = OwnerClientId;
        Vector3 bodyOrigin = GetBodyDropOrigin();
        var dropped = new HashSet<NetworkObject>();
        int stowedIndex = 0;

        void Drop(NetworkObjectReference itemRef, bool useItemPosition)
        {
            if (!itemRef.TryGet(out NetworkObject netObj) || netObj == null || !netObj.IsSpawned) return;
            if (!dropped.Add(netObj)) return;

            PickableObject item = netObj.GetComponent<PickableObject>();
            if (item == null) return;

            // Only release items this player actually still holds (hand or stowed).
            if (item.HolderClientId != ownerId) return;

            Vector3 position;
            if (useItemPosition && netObj.gameObject.activeInHierarchy)
            {
                position = item.transform.position;
            }
            else
            {
                // Stowed items are hidden at a stale stow pose — spawn them just above the body,
                // spread a little so they don't stack inside each other.
                float angle = stowedIndex++ * 2.4f;
                position = bodyOrigin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.3f;
            }

            item.ForceDropWithPhysicsServer(position, Vector3.zero);
        }

        if (_pickup != null)
            Drop(_pickup.HeldObjectRef, useItemPosition: true);

        Drop(_firstSlotRef.Value, useItemPosition: false);
        Drop(_secondSlotRef.Value, useItemPosition: false);
    }

    /// <summary>A point slightly above the (ragdolling) body, used for stowed-item drops.</summary>
    private Vector3 GetBodyDropOrigin()
    {
        Transform hips = _animator != null && _animator.isHuman
            ? _animator.GetBoneTransform(HumanBodyBones.Hips)
            : null;

        Vector3 origin = hips != null ? hips.position
                       : stowPoint != null ? stowPoint.position
                       : transform.position + Vector3.up;

        return origin + Vector3.up * 0.3f;
    }

    /// <summary>
    /// Owner-only local teardown on death: forgets the held and stowed items without sending any
    /// drop RPCs (the server already dropped them — see <see cref="DropAllItemsOnDeathServer"/>).
    /// </summary>
    public void ClearAllSlotsLocalOnDeath()
    {
        if (!IsOwner) return;

        // Host player: OnDeath subscriber order isn't guaranteed, so make sure the server drop
        // reads the held/slot references before this local teardown clears them. Idempotent —
        // already-dropped items no longer list this player as holder.
        if (IsServer)
            DropAllItemsOnDeathServer();

        _pickup.ReleaseHeldObjectLocallyForServerDrop();

        for (int i = 0; i < _slots.Length; i++)
        {
            PickableObject item = _slots[i];
            if (item == null) continue;

            if (_stowed[i])
            {
                // Mirror the server's un-stow locally right away so the item isn't left inactive
                // on this machine until the replicated stowed flag arrives.
                item.gameObject.SetActive(true);
                item.RemoveParent();
                item.OnDropped();
            }

            ClearSlot(i);
        }

        _unslottedHeld = null;
        SetActiveSlot(-1);
    }

    // ── Self-healing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Owner-only, every frame. The hotbar only learns about hand changes through
    /// <see cref="PlayerPickupController.OnHeldObjectChanged"/> and its own stow calls, so any
    /// system that removes, hides or releases an item through another path (despawn, forced
    /// release, scripted transition) used to leave a slot claiming "in hand" while the hand was
    /// empty. Pressing that slot's key then tried to STOW an empty hand and did nothing, so the
    /// item looked lost while its icon stayed in the HUD. This re-derives a consistent state:
    /// <list type="bullet">
    /// <item>destroyed/despawned item → slot cleared;</item>
    /// <item>stowed item that reappeared in the world (force-released) → slot cleared;</item>
    /// <item>"in hand" item that isn't actually held → hidden: treated as stowed (or put straight
    /// back in an empty hand); still registered to us in the world: back to an empty hand or
    /// dropped; claimed by someone else: slot cleared.</item>
    /// </list>
    /// </summary>
    private void ReconcileSlotState()
    {
        if (_pickup == null) return;

        PickableObject held = _pickup.HeldObject;

        for (int i = 0; i < _slots.Length; i++)
        {
            PickableObject item = _slots[i];
            if (ReferenceEquals(item, null)) continue;

            if (item == null || !item.IsSpawned)
            {
                Debug.LogWarning($"[PlayerInventory] Slot {i + 1} item was despawned/destroyed while in the inventory — clearing slot.");
                ForgetSlot(i);
                continue;
            }

            if (_stowed[i])
            {
                // Stowing deactivates the item synchronously on the owner, so an active "stowed"
                // item means another system released it back into the world.
                if (item.gameObject.activeSelf)
                {
                    Debug.LogWarning($"[PlayerInventory] Stowed '{item.name}' (slot {i + 1}) was released to the world externally — clearing slot.");
                    ForgetSlot(i);
                }
                continue;
            }

            if (held == item) continue;

            bool hiddenOnBody   = !item.gameObject.activeSelf;
            bool heldByAnother  = item.HolderClientId != ulong.MaxValue && item.HolderClientId != OwnerClientId;

            Debug.LogWarning($"[PlayerInventory] Slot {i + 1} '{item.name}' was marked in-hand but the hand holds " +
                             $"'{(held != null ? held.name : "nothing")}' (itemActive={!hiddenOnBody}, holder={item.HolderClientId}). Repairing hotbar state.");

            if (heldByAnother)
            {
                ForgetSlot(i);
                continue;
            }

            if (hiddenOnBody)
            {
                // Recoverable: make it a proper stowed item, and if the hand is free put it back.
                _stowed[i] = true;
                if (_activeSlot == i) SetActiveSlot(-1);
                if (held == null && _unslottedHeld == null)
                {
                    _pickup.UnstowItemToHand(item);   // HandleHeldObjectChanged re-marks it active
                    held = _pickup.HeldObject;
                }
                continue;
            }

            if (held == null && _unslottedHeld == null && item.HolderClientId == OwnerClientId)
            {
                _stowed[i] = true;
                if (_activeSlot == i) SetActiveSlot(-1);
                _pickup.UnstowItemToHand(item);
                held = _pickup.HeldObject;
                continue;
            }

            if (item.HolderClientId == OwnerClientId)
            {
                // Still registered to us but the hand is busy — release it where it is.
                item.RemoveParent();
                item.ReleaseHolderServerRpc();
                item.DropServerRpc(item.transform.position, item.transform.rotation);
                item.OnDropped();
            }

            ForgetSlot(i);
        }

        // An unslotted item that is no longer in hand must not keep the hotbar locked.
        if (!ReferenceEquals(_unslottedHeld, null) && (_unslottedHeld == null || _pickup.HeldObject != _unslottedHeld))
            _unslottedHeld = null;

        if (_activeSlot >= 0 && (_slots[_activeSlot] == null || _stowed[_activeSlot] || _pickup.HeldObject != _slots[_activeSlot]))
            SetActiveSlot(-1);
    }

    private void ForgetSlot(int index)
    {
        ClearSlot(index);
        if (_activeSlot == index) SetActiveSlot(-1);
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
