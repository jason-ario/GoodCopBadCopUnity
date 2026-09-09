using System;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A junk item spawned by TakeOutTrashTask. Collected by a player who is holding a non-full
/// TrashBag — either by pressing E (Interact) or by left-clicking while holding the bag
/// (InteractWithItem). Despawns on collection and fills the bag by one unit.
///
/// Prefab requirements:
///   - NetworkObject (or a parent NetworkObject when used on a SuspectCharacter)
///   - HighlightEffect  (required by Interactable)
///   - Collider on the Interactable layer
///   - Trash Bag PickableItemData assigned to itemsThatCanInteractWith in the Inspector
/// Standalone junk prefabs must be registered as Network Prefabs in the NetworkManager.
/// When added to a SuspectCharacter, it starts non-collectible — SuspectCharacter.EnableJunkPickup()
/// marks it collectible (via IsCollectible) on death. This component's own Unity 'enabled' flag is
/// never toggled; see IsCollectible for why.
/// </summary>
public class JunkItem : Interactable
{
    /// <summary>Default interaction label shown when targeting a junk item.</summary>
    public const string DefaultInteractText = "Collect Junk";

    /// <summary>
    /// Fired on the server each time a JunkItem is successfully collected and despawned.
    /// Subscribe server-side to track how many items remain in the scene.
    ///
    /// Carries the collected item so listeners can tell WHICH item it was — <see cref="TakeOutTrashTask"/>
    /// needs the identity to distinguish a pickup that belongs to its counted total from an
    /// out-of-region bonus pickup (a corpse or gore chunk from beyond the checkpoint fence), which
    /// is credited at deposit time instead of being required.
    /// </summary>
    public static event Action<JunkItem> OnAnyJunkItemCollected;

    /// <summary>
    /// Optional server-side callback fired when this specific item is collected.
    /// When assigned, allows owning systems (e.g. CleanBoothMessTask) to track
    /// their own spawned junk independently of the global static event.
    /// Set this immediately after spawning on the server.
    /// </summary>
    [System.NonSerialized] public System.Action OnCollected;

    /// <summary>
    /// Server-authoritative "is this currently collectible" flag. Replaces toggling this
    /// component's own Unity 'enabled' flag: doing so at runtime (enabling a JunkItem added to
    /// a SuspectCharacter once the suspect dies) made this NetworkBehaviour's inclusion in
    /// Netcode's scene-object synchronization stream diverge between the server (enabled) and
    /// any client that joins afterward (freshly-loaded, still disabled), corrupting that
    /// client's sync buffer and crashing it. Being a NetworkVariable, its current value is
    /// delivered correctly to every client — including late joiners — via ordinary replication.
    /// </summary>
    public readonly NetworkVariable<bool> IsCollectible = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Cleanup-zone membership is owned by TakeOutTrashTask; it never disables pickup affordances.

    /// <summary>
    /// Server-authoritative task-ownership flag. A junk item can always be baggable when
    /// <see cref="CanBeCollected"/> is true, but it only receives the persistent objective
    /// highlight and compass marker while an active task requires it.
    /// </summary>
    public readonly NetworkVariable<bool> IsTaskRequired = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Server-authoritative tutorial call-out glow, driven by
    /// <see cref="TakeOutTrashTask.HighlightAllItemsForTutorial"/>.
    ///
    /// A NetworkVariable for the same reason as <see cref="IsCollectible"/>: this used to be pushed
    /// as a one-shot ClientRpc carrying a list of item references, which reaches only the clients
    /// connected at that instant. A player who joined mid-tutorial saw none of the call-out glow and
    /// had no way to find the junk the objective was asking for. Replication also fixes the
    /// narrower ordering bug in the old approach — junk spawned *after* the broadcast never lit up,
    /// because nothing re-sent it.
    /// </summary>
    public readonly NetworkVariable<bool> TutorialHighlight = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Server-only. Turns the tutorial call-out glow on or off for every peer.</summary>
    public void SetTutorialHighlight(bool highlight)
    {
        if (!IsServer) return;
        TutorialHighlight.Value = highlight;
    }

    /// <summary>
    /// Server-only. Marks whether a currently collectible item is required by an active task.
    /// This controls only objective presentation, never bag pickup availability.
    /// </summary>
    public void SetTaskRequired(bool required)
    {
        if (!IsServer) return;
        IsTaskRequired.Value = required;
    }

    /// <summary>
    /// When true (default), collecting this item destroys its NetworkObject outright. Set false
    /// in the Inspector for junk that lives on a GameObject meant to be reused later (e.g. a
    /// guard corpse occupying a GuardPurchasePoint's reusable soldier slot) — collection then
    /// only flips <see cref="IsCollectible"/> back to false instead of destroying anything,
    /// leaving the owning script (via <see cref="OnCollected"/>) responsible for hiding/resetting
    /// the GameObject so it can be brought back for a future spawn.
    /// </summary>
    [Tooltip("When true (default), collecting this junk item destroys its NetworkObject. Set " +
             "false for junk on a reusable GameObject (e.g. a guard corpse) — collection just " +
             "flips IsCollectible off instead, leaving cleanup/reuse to the owning script.")]
    [SerializeField] private bool _destroyOnCollect = true;

    /// <summary>Server-only. Marks this body as collectible junk (or clears it). Does not touch
    /// this component's Unity 'enabled' flag — see <see cref="IsCollectible"/>.</summary>
    public void SetCollectible(bool collectible)
    {
        if (!IsServer) return;
        IsCollectible.Value = collectible;
    }

    /// <summary>
    /// Cached <see cref="SuspectCharacter"/> on this GameObject, if any. Suspect bodies are the one
    /// case where this component's Unity 'enabled' flag is meaningless (it is deliberately left off
    /// and never toggled — see <see cref="IsCollectible"/>), so collectibility has to be read from
    /// <see cref="IsCollectible"/> instead. Null for ordinary trash and mutant gore/corpses.
    /// </summary>
    private SuspectCharacter _suspect;

    /// <summary>
    /// True when this is live, collectible junk rather than a dormant component or a still-living
    /// character. Zone membership is intentionally not part of this predicate: dead mutants and gore
    /// remain baggable wherever they land, while <see cref="TakeOutTrashTask"/> separately decides
    /// whether an item advances a cleanup task.
    ///
    /// This is the single pickup-affordance predicate: it drives whether the item can be targeted
    /// at all (<see cref="IsInteractable"/>). Objective presentation additionally requires the
    /// replicated <see cref="IsTaskRequired"/> flag in <see cref="JunkPickupHighlightService"/>.
    ///
    /// Collection additionally requires a non-full trash bag (see <see cref="Interact"/>); that is a
    /// property of the player, not of the junk, so it is intentionally not part of this.
    /// </summary>
    public bool CanBeCollected
    {
        get
        {
            if (!gameObject.activeInHierarchy) return false;

            if (_suspect != null) return IsCollectible.Value;

            return enabled;
        }
    }

    /// <summary>
    /// Junk is targetable exactly when it is collectible, so the reticle can never offer a pickup the
    /// item will refuse. Cleanup-zone membership affects task credit only, never this affordance.
    /// </summary>
    public override bool IsInteractable => CanBeCollected;

    protected override void Awake()
    {
        base.Awake();

        _suspect = GetComponent<SuspectCharacter>();

        if (string.IsNullOrEmpty(interactText))
            interactText = DefaultInteractText;
    }

    /// <summary>
    /// Junk glowing because an active task requires it uses the softer amber profile, so a yard
    /// scattered with optional gore reads as scenery rather than as a screenful of objectives.
    /// </summary>
    protected override HighlightProfile ForceHighlightProfile => JunkPickupHighlightService.CollectibleProfile;

    // ── Findability highlight registration ────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Re-evaluate the glow when a body becomes (or stops being) collectible mid-run — e.g. a
        // suspect corpse on death or a reusable guard corpse being cleared after collection.
        IsCollectible.OnValueChanged += OnIsCollectibleChanged;
        IsTaskRequired.OnValueChanged += OnIsTaskRequiredChanged;
        TutorialHighlight.OnValueChanged += OnTutorialHighlightChanged;

        // Apply the current value for late joiners — an item already called out by the tutorial
        // must light up for a client that connects after the call-out was issued.
        if (TutorialHighlight.Value)
            SetForceHighlight(true, HighlightHold.Tutorial);

        JunkPickupHighlightService.Register(this);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        IsCollectible.OnValueChanged -= OnIsCollectibleChanged;
        IsTaskRequired.OnValueChanged -= OnIsTaskRequiredChanged;
        TutorialHighlight.OnValueChanged -= OnTutorialHighlightChanged;

        JunkPickupHighlightService.Unregister(this);
    }

    private void OnTutorialHighlightChanged(bool previous, bool current)
    {
        SetForceHighlight(current, HighlightHold.Tutorial);
    }

    /// <summary>
    /// Overrides (rather than hides) <see cref="NetworkBehaviour.OnDestroy"/> so Netcode's own
    /// teardown still runs — declaring a plain private OnDestroy here would shadow the virtual base
    /// and silently skip it.
    /// </summary>
    public override void OnDestroy()
    {
        JunkPickupHighlightService.Unregister(this);
        base.OnDestroy();
    }

    /// <summary>
    /// Mutant corpses and gore chunks become collectible by having this component enabled at runtime
    /// (see <c>MutantEnemy.ApplyCorpseJunkPickupState</c>), which fires OnEnable rather than changing
    /// any NetworkVariable — so re-register here to light the body up the instant it settles instead
    /// of leaving it dark.
    /// </summary>
    private void OnEnable()
    {
        if (IsSpawned)
            JunkPickupHighlightService.Register(this);
    }

    private void OnDisable()
    {
        JunkPickupHighlightService.Unregister(this);
    }

    private void OnIsTaskRequiredChanged(bool previous, bool current)
    {
        if (!IsSpawned) return;

        JunkPickupHighlightService.Refresh(this);
    }

    private void OnIsCollectibleChanged(bool previous, bool current)
    {
        if (!IsSpawned) return;

        JunkPickupHighlightService.Refresh(this);
    }

    // ── Interaction ───────────────────────────────────────────────────────────

    /// <summary>
    /// Triggered by the E key. If the player is holding a non-full TrashBag, collects
    /// this item. Does nothing when empty-handed or when the bag is already full.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        TrashBag bag = player.pickupController.HeldObject as TrashBag;
        if (bag == null || bag.IsFull) return;

        CollectServerRpc(bag.NetworkObject);
    }

    /// <summary>
    /// Triggered by left-click while holding a compatible item (TrashBag). Collects this
    /// junk item into the bag.
    /// </summary>
    public override void InteractWithItem(PlayerInteractionController player, PickableObject heldItem)
    {
        TrashBag bag = heldItem as TrashBag;
        if (bag == null || bag.IsFull) return;

        CollectServerRpc(bag.NetworkObject);
    }

    // ── Server RPC ────────────────────────────────────────────────────────────

    /// <summary>
    /// Server-side collection: re-validates bag capacity to guard against race conditions,
    /// increments the bag's junk count, then despawns this item.
    /// RequireOwnership = false so any client can trigger collection.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void CollectServerRpc(NetworkObjectReference bagRef)
    {
        if (!bagRef.TryGet(out NetworkObject bagNetObj))
        {
            Debug.LogWarning("[JunkItem] CollectServerRpc: bag NetworkObject not found.");
            return;
        }

        TrashBag bag = bagNetObj.GetComponent<TrashBag>();

        if (bag == null)
        {
            Debug.LogWarning("[JunkItem] CollectServerRpc: NetworkObject has no TrashBag component.");
            return;
        }

        if (bag.IsFull) return;

        bag.AddJunk();

        // Despawn BEFORE firing the collection events: TakeOutTrashTask.OnJunkItemCollected
        // prunes its tracked list by checking NetworkObject.IsSpawned, so listeners must see
        // this item as already despawned, otherwise the just-collected item survives that
        // prune and is only removed on the *next* collection — permanently leaking a stale
        // "ghost" entry for the last item of any run (nothing ever prunes it again), which
        // keeps ThreatLevel (and therefore CheckpointIntegrityService's score) from ever
        // fully reaching 0/100% even once everything visible has been cleaned up.
        //
        // _destroyOnCollect is false for junk on a reusable GameObject (e.g. a guard corpse) —
        // those are left spawned and alive, just no longer collectible; OnCollected below is
        // responsible for hiding/resetting them for reuse.
        if (_destroyOnCollect)
            NetworkObject.Despawn(destroy: true);
        else
        {
            IsTaskRequired.Value = false;
            TutorialHighlight.Value = false;
            IsCollectible.Value = false;
        }

        OnCollected?.Invoke();
        OnAnyJunkItemCollected?.Invoke(this);
    }
}
