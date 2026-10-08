using System.Collections.Generic;
using GoodCopBadCop.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Two-slot hotbar inventory for the local player.
/// Press 1/2 to equip the item in that slot, scroll the mouse wheel, or press the gamepad
/// <see cref="GameAction.NextSlot"/> / <see cref="GameAction.PreviousSlot"/> bindings (D-pad
/// Right / Left by default) to cycle through held/carried items. Hotbar input is ignored while
/// the guidebook or a diegetic view is open. Picking up an item brings it straight to hand;
/// placing/dropping empties the hand.
///
/// Exactly one slot is always SELECTED (<see cref="ActiveSlot"/> is 0 or 1, never -1). The hand
/// always shows the selected slot's contents: its item when occupied, or empty hands when that
/// slot is empty. The other slot's item (if any) is stowed (hidden on body) via
/// <see cref="stowPoint"/>. Pressing a slot's key selects it — stowing the held item and bringing
/// the new slot's item (if any) to hand — and pressing the selected slot's own key is a no-op.
/// Picking something up with empty hands puts it in the selected (empty) slot; placing/dropping
/// it empties that slot but keeps it selected, so the HUD still shows it as equipped.
/// Non-stowable items (and any pickup that finds no slot) are carried unslotted and lock the
/// hotbar until placed/dropped (see <see cref="IsHandLocked"/>).
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

    // Owner-write mirror of the selected slot so spectators' HUDs can highlight it even when empty.
    private readonly NetworkVariable<int> _selectedSlotNet = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private int _activeSlot = 0;   // selected slot; always 0 or 1 (may be empty = empty hands)

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

    /// <summary>Fired when the selected (equipped) slot index changes. Always 0 or 1; the slot may be empty.</summary>
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
    /// Selected slot as seen by any peer (owner-written mirror). Always 0 or 1, even when that
    /// slot is empty.
    /// </summary>
    public int ReplicatedActiveSlot => _selectedSlotNet.Value;

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

        _selectedSlotNet.Value = _activeSlot;
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

        bool hotkeyPressed = Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2);
        string inputBlock = GetHotbarInputBlockReason();
        if (inputBlock != null)
        {
            if (hotkeyPressed)
                Debug.LogWarning($"[PlayerInventory] Hotbar key ignored: {inputBlock}.");
            return;
        }

        if (!TextInputFocus.IsCapturingKeyboard)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) EquipSlot(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) EquipSlot(1);
        }
        else if (hotkeyPressed)
        {
            Debug.LogWarning("[PlayerInventory] Hotbar key ignored: a text field is capturing the keyboard.");
        }
        if (ReloadPressed()) TryReloadActiveWeapon();

        float scroll = Input.mouseScrollDelta.y;
        if (scroll > 0f) CycleActiveItem(1);
        else if (scroll < 0f) CycleActiveItem(-1);

        // Gamepad slot cycling (D-pad Right / Left by default, rebindable).
        if (RebindableInput.GetGamepadDown(GameAction.NextSlot)) CycleActiveItem(1);
        else if (RebindableInput.GetGamepadDown(GameAction.PreviousSlot)) CycleActiveItem(-1);
    }

    /// <summary>
    /// Returns why hotbar input is currently gated, or null when it's accepted. A locked hotbar
    /// still receives legacy hotkey/wheel input, so cutscenes, dialogue, modal screens (end-of-shift
    /// report, diegetic views, PC, popups) and pause must all swallow it — otherwise an unseen
    /// press could stow/swap an item behind them.
    /// </summary>
    private static string GetHotbarInputBlockReason()
    {
        PlayerInstance player = PlayerInstance.Instance;
        if (player != null)
        {
            if (player.IsInCutscene) return "player is in a cutscene";
            if (ScriptedDialogueRunner.IsScriptedModeActive) return "scripted dialogue is active";
            if (DialogueChoiceSystem.IsInDialogueMode) return "dialogue choice mode is active";
            if (!player.CanControl) return "player control is disabled (CanControl=false)";
            if (player.IsViewingShiftReport) return "shift report is open";
        }

        if (UIController.Instance != null && UIController.Instance.IsPaused) return "game is paused";
        // The guidebook turns pages with the D-pad, and diegetic views own their own input.
        if (GuidebookController.IsLocalGuidebookOpen) return "guidebook is open";
        if (DiegeticViewController.Current != null) return "a diegetic view is open";
        return null;
    }

    /// <summary>
    /// Scroll-wheel slot cycling. Steps the selection one slot forward/backward (wrapping), via
    /// <see cref="EquipSlot"/>. With two slots either direction simply selects the other slot,
    /// whether or not it holds an item.
    /// </summary>
    /// <param name="direction">+1 to scroll to the next slot, -1 for the previous.</param>
    private void CycleActiveItem(int direction)
    {
        if (!IsOwner) return;

        // An unslotted item in hand locks the hotbar — the player has to put it down.
        if (IsHandLocked) return;

        int count = _slots.Length;
        int next = ((_activeSlot + direction) % count + count) % count;
        EquipSlot(next);
    }

    // ── Reloading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="GameAction.Reload"/> (R by default, rebindable in Settings -> Controls) or its
    /// rebindable gamepad binding (Y / buttonNorth by default).
    /// Ignored while paused so the gamepad button can't fire through the pause menu.
    /// </summary>
    private static bool ReloadPressed()
    {
        if (UIController.Instance != null && UIController.Instance.IsPaused) return false;
        if (RebindableInput.GetKeyDown(GameAction.Reload)) return true;
        return RebindableInput.GetGamepadDown(GameAction.Reload);
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
            // shown, and the selection is kept (but locked) while it is held.
            if (!IsStowable(obj))
            {
                _unslottedHeld = obj;
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

            // Brand-new item — it goes into the selected slot (empty whenever the hand is empty).
            // Fall back to any free slot only if the selected one is unexpectedly occupied by a
            // stowed item; that item stays stowed and the new slot becomes selected.
            int target = _slots[_activeSlot] == null ? _activeSlot : FreeSlot();
            if (target < 0)
            {
                // No slot to put it in — carry it unslotted (like a non-stowable item) until it's
                // placed/dropped.
                _unslottedHeld = obj;
                return;
            }

            if (target != _activeSlot && _slots[_activeSlot] != null)
                _stowed[_activeSlot] = true;

            _slots[target]  = obj;
            _stowed[target] = false;
            SetSlotReference(target, obj);
            OnSlotChanged?.Invoke(target, obj);
            SetActiveSlot(target);
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

            // Hand became empty via normal drop or place (not a stow operation). The slot empties
            // but stays selected, so the HUD keeps showing it as equipped.
            if (_slots[_activeSlot] != null && !_stowed[_activeSlot])
                ClearSlot(_activeSlot);
        }
    }

    // ── Slot equipping ────────────────────────────────────────────────────────

    /// <summary>
    /// Selects a hotbar slot. Selecting the other slot stows the held item (if any) and brings
    /// the newly selected slot's item to hand — or leaves the hand empty if that slot is empty.
    /// Pressing the already-selected slot's key is a no-op (apart from self-healing an item that
    /// should be in hand but isn't). With both slots full, an item is therefore always in hand.
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
        if (IsHandLocked)
        {
            Debug.LogWarning($"[PlayerInventory] Can't equip slot {slotIndex + 1}: hand holds unslotted '{_unslottedHeld.name}' — place or drop it first.");
            return;
        }

        PickableObject target = _slots[slotIndex];
        PickableObject held   = _pickup.HeldObject;

        // ── Pressing the selected slot's own key ──────────────────────────────
        if (slotIndex == _activeSlot)
        {
            // Self-heal: the selected slot's item should be in hand. If it's stowed (or flagged
            // in-hand while the hand is actually empty), bring it back.
            if (target != null && held == null)
            {
                Debug.LogWarning($"[PlayerInventory] Selected slot {slotIndex + 1} '{target.name}' wasn't in hand — re-equipping it.");
                _stowed[slotIndex] = true;
                _pickup.UnstowItemToHand(target);
                WarnIfUnstowFailed(slotIndex, target);
            }
            return;
        }

        // ── Selecting the other slot: stow whatever is in hand first ──────────
        if (held != null)
        {
            int currentSlot = _activeSlot;
            if (_slots[currentSlot] != held)
            {
                // Hand holds something the selected slot doesn't own — stowing it would orphan it.
                Debug.LogWarning($"[PlayerInventory] Can't select slot {slotIndex + 1}: hand holds '{held.name}' which isn't the selected slot's item (selected={currentSlot + 1}).");
                return;
            }

            if (stowPoint == null)
            {
                Debug.LogWarning("[PlayerInventory] stowPoint is not assigned — slot switching disabled while holding an item.");
                return;
            }

            PickableObject stowedItem = _pickup.StowCurrentItemToPoint(stowPoint);
            if (stowedItem == null) return;   // couldn't free the hand — keep the current selection
            _stowed[currentSlot] = true;
        }

        SetActiveSlot(slotIndex);

        // Selected slot is empty → empty hands. Otherwise bring its item to hand.
        if (target == null) return;

        _stowed[slotIndex] = true;
        _pickup.UnstowItemToHand(target);
        // _stowed[slotIndex] = false is set in HandleHeldObjectChanged.
        WarnIfUnstowFailed(slotIndex, target);
    }

    /// <summary>
    /// <see cref="PlayerPickupController.UnstowItemToHand"/> can bail out silently (e.g. the item
    /// reads as held by another client). Log the state so a "slot won't equip" repro names the
    /// cause instead of looking like a dead key.
    /// </summary>
    private void WarnIfUnstowFailed(int slotIndex, PickableObject target)
    {
        if (target == null) return;

        if (_pickup.HeldObject == target)
        {
            if (!target.gameObject.activeInHierarchy)
                Debug.LogWarning($"[PlayerInventory] Slot {slotIndex + 1} '{target.name}' is in hand but its GameObject is inactive (invisible).");
            return;
        }

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
        // Selection is kept — both slots are now empty, so the selected one just shows as equipped.
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

            if (held == item)
            {
                // In hand but hidden: a late stowed=true replication deactivated it after it was
                // already brought back to hand. Show it again and re-assert the unstowed state.
                if (!item.gameObject.activeSelf)
                {
                    Debug.LogWarning($"[PlayerInventory] Slot {i + 1} '{item.name}' is in hand but was hidden — re-showing it.");
                    item.gameObject.SetActive(true);
                    item.RequestSetStowedNetworked(false);
                }
                continue;
            }

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
                // Recoverable: make it a proper stowed item, and if it's the selected slot and the
                // hand is free, put it back in hand.
                _stowed[i] = true;
                if (i == _activeSlot && held == null && _unslottedHeld == null)
                {
                    _pickup.UnstowItemToHand(item);   // HandleHeldObjectChanged re-marks it in hand
                    held = _pickup.HeldObject;
                }
                continue;
            }

            if (held == null && _unslottedHeld == null && item.HolderClientId == OwnerClientId)
            {
                _stowed[i] = true;
                _pickup.UnstowItemToHand(item);   // selects this slot via HandleHeldObjectChanged
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
    }

    private void ForgetSlot(int index) => ClearSlot(index);

    // ── Internal helpers ──────────────────────────────────────────────────────

    private void SetActiveSlot(int index)
    {
        if (index < 0 || index >= _slots.Length) return;
        if (_activeSlot == index) return;

        _activeSlot = index;
        if (IsOwner && IsSpawned)
            _selectedSlotNet.Value = index;
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
