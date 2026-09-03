using System;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    void Interact(PlayerInteractionController player);
}

/// <summary>
/// Marker interface for interactables that act as pickup slots (e.g. InkStamp).
/// Left-clicking one with empty hands should call Interact(), just like a PickableObject.
/// </summary>
public interface IPickupSlot { }

/// <summary>
/// Marker interface for interactables that handle LMB interaction regardless of
/// what the player is holding. When implemented, <see cref="PlayerInteractionController"/>
/// will call <see cref="IInteractable.Interact"/> via <c>TryItemUse</c> instead of
/// routing to <c>TryUseObject</c> when the held item is not compatible.
/// </summary>
public interface IHeldItemPassthrough { }

/// <summary>
/// Independent reasons an <see cref="Interactable"/>'s highlight can be held on regardless of
/// hover state. Held as flags so two systems can request the highlight at the same time and
/// neither one clearing its own hold turns off the other's — e.g. a tutorial call-out on a gore
/// pile that is also glowing because it's collectible junk.
/// </summary>
[System.Flags]
public enum HighlightHold
{
    None = 0,

    /// <summary>Scripted call-out that stays lit until a task event clears it (the default).</summary>
    Tutorial = 1 << 0,

    /// <summary>
    /// "This is collectible junk" findability glow, held for as long as the item is pickable —
    /// see <see cref="JunkPickupHighlightService"/>.
    /// </summary>
    PickupAffordance = 1 << 1,
}

[RequireComponent(typeof(HighlightEffect))]
public abstract class Interactable : NetworkBehaviour, IInteractable
{
    HighlightEffect highlightEffect;
    public PickableItemData[] itemsThatCanInteractWith;
    public UnityAction OnInteract;
    public UnityAction OnInteractWithItem;
    public string interactText;

    /// <summary>
    /// When true, the reticle will display an extract hint (key icon + action text)
    /// next to the reticle while this object is targeted.
    /// Override to true in subclasses that have a meaningful E-key action (e.g. <see cref="ContainerPickableObject"/>).
    /// </summary>
    public virtual bool ShowInteractHint => false;

    /// <summary>
    /// Only highlight children whose names contain this string.
    /// Leave empty to disable highlighting entirely.
    /// </summary>
    [Tooltip("Only highlight children whose names contain this string. Leave empty to disable highlighting entirely.")]
    [SerializeField] private string highlightNameFilter;

    /// <summary>
    /// Which systems currently want this object's highlight held on regardless of hover state.
    /// Normal hover-driven <see cref="Highlight"/>(false) calls from
    /// <see cref="PlayerInteractionController"/> are ignored while any flag is set.
    /// Used for tutorial call-outs (e.g. <see cref="TakeOutTrashTask.HighlightAllItemsForTutorial"/>)
    /// and for the collectible-junk findability glow (see <see cref="JunkPickupHighlightService"/>).
    /// Flags rather than a bool so one system releasing its hold can't switch off another's.
    /// </summary>
    private HighlightHold _highlightHolds;

    /// <summary>True while any system is holding this object's highlight on.</summary>
    public bool IsForceHighlighted => _highlightHolds != HighlightHold.None;

    /// <summary>
    /// The profile this object's <see cref="HighlightEffect"/> was authored with in the Inspector
    /// (normally the shared "Highlighted" asset). Cached in <see cref="Awake"/> because
    /// <see cref="HighlightEffect.ProfileLoad"/> overwrites <c>HighlightEffect.profile</c> — without
    /// this there would be no record of the original style to swap back to.
    /// </summary>
    private HighlightProfile _defaultProfile;

    /// <summary>Profile currently loaded into the effect, so repeat swaps are free.</summary>
    private HighlightProfile _appliedProfile;

    /// <summary>True while the reticle is on this object (see <see cref="Highlight"/>).</summary>
    private bool _hovered;

    /// <summary>
    /// Optional alternate style used while the highlight is being HELD on by a non-hover source
    /// (see <see cref="HighlightHold"/>) and the player is not actually aiming at the object.
    /// Lets a persistent, ambient glow read differently from the sharp "you are targeting this"
    /// hover highlight — <see cref="JunkItem"/> uses it for the softer amber findability glow so a
    /// yard full of lit-up gore doesn't look like a yard full of active interaction prompts.
    /// Return null (the default) to always use the authored profile.
    /// </summary>
    protected virtual HighlightProfile ForceHighlightProfile => null;

    /// <summary>
    /// Whether this object can be interacted with RIGHT NOW. <see cref="PlayerInteractionController"/>
    /// tests this instead of the raw <c>enabled</c> flag when deciding whether the reticle may target
    /// it, so a subclass whose availability depends on runtime state can express that without having
    /// to toggle its own <c>enabled</c> flag — which for a <see cref="NetworkBehaviour"/> is not safe
    /// to do after spawn (it desynchronises Netcode's behaviour ordering for late joiners; see
    /// <see cref="JunkItem.IsCollectible"/>).
    ///
    /// Overriding this rather than adding checks in the controller is what keeps targeting and
    /// highlighting from disagreeing: <see cref="JunkItem"/> answers both from one predicate, so an
    /// item can never glow while refusing to be picked up, or vice versa.
    /// </summary>
    public virtual bool IsInteractable => enabled;

    public virtual void Interact(PlayerInteractionController player)
    {
        OnInteract?.Invoke();
    }

    /// <summary>
    /// Called when the player presses E while this interactable is targeted.
    /// Distinct from <see cref="Interact"/> which is triggered by LMB (when empty-handed).
    /// Defaults to calling <see cref="Interact"/> so existing subclasses require no changes.
    /// Override in subclasses that need separate E vs LMB behaviour
    /// (e.g. <see cref="ContainerPickableObject"/> picks up on LMB and extracts on E).
    /// </summary>
    public virtual void InteractAlternate(PlayerInteractionController player)
    {
        Interact(player);
    }

    protected virtual void Awake()
    {
        highlightEffect = GetComponent<HighlightEffect>();
        // Remember the authored style BEFORE any ProfileLoad call overwrites the field
        // (see _defaultProfile), so ForceHighlightProfile swaps are always reversible.
        _defaultProfile = highlightEffect.profile;
        _appliedProfile = _defaultProfile;
        // ProfileLoad applies visual style settings from the profile but does not
        // touch the 'highlighted' state, so call it first.
        highlightEffect.ProfileLoad(highlightEffect.profile);
        // Force the component enabled so that HighlightEffect.Start() always runs and
        // SetupMaterial() builds the renderer list (rms). If a prefab was saved with
        // the component disabled, Start() never fires, leaving rms null and causing the
        // first hover to silently fail to render. Visibility is gated by 'highlighted'.
        highlightEffect.enabled = true;
        highlightEffect.highlighted = false;

        if (!string.IsNullOrEmpty(highlightNameFilter))
            highlightEffect.effectNameFilter = highlightNameFilter;
    }

    public virtual void Highlight(bool highlight)
    {
        _hovered = highlight;

        // While any hold is active (tutorial call-out, pickup affordance), ignore hover-driven
        // attempts to turn the highlight off — it should only clear via SetForceHighlight(false).
        // The style still has to change back to the hold's own (softer) profile, since the object
        // stays lit but is no longer the thing the player is aiming at.
        if (_highlightHolds != HighlightHold.None && !highlight)
        {
            ApplyHighlightProfile();
            return;
        }

        // Some subclasses (e.g. WorldPurchaseActionInteractable / WorldShopItemInteractable)
        // share this HighlightEffect with a ShopItem component, which drives its own
        // hover-highlight system by toggling 'enabled' directly (SetHighlightBlocked).
        // If that leaves 'enabled' false (e.g. after the purchase popup closes), our
        // 'highlighted'-driven visibility below would silently never render. Reassert
        // 'enabled' every call so this class's invariant — always enabled, visibility
        // solely via 'highlighted' — holds no matter what else touched the component.
        highlightEffect.enabled = true;

        // Hovering always shows the authored (default) style, even on an object that is also being
        // held lit by another system — the hover highlight is the targeting feedback.
        ApplyHighlightProfile();

        // Drive visibility through 'highlighted', not 'enabled'.
        // Toggling 'enabled' was the cause of the broken-first-hover bug: each
        // enable/disable cycle re-ran OnEnable before Start, leaving rms uninitialised.
        highlightEffect.highlighted = highlight;

        if (highlight)
        {
            OnHighlight();
        }
        else
        {
            OnStopHighlight();
        }
    }

    /// <summary>
    /// Turns on (or off) a persistent highlight that is not cleared by the normal
    /// hover-driven <see cref="Highlight"/>(false) calls made every frame by
    /// <see cref="PlayerInteractionController"/> when the reticle looks away.
    /// Intended for tutorial call-outs where the object should stay highlighted until a
    /// specific game event (e.g. the item being collected/despawned) clears it, rather
    /// than just until the player stops looking at it.
    /// </summary>
    public void SetForceHighlight(bool force) => SetForceHighlight(force, HighlightHold.Tutorial);

    /// <summary>
    /// Adds or removes <paramref name="source"/>'s claim on this object's persistent highlight.
    /// The highlight stays visible while ANY source still holds it, so independent systems can
    /// request it concurrently without clobbering each other — releasing the pickup-affordance
    /// hold won't switch off a tutorial call-out that is also active, and vice versa.
    /// </summary>
    public void SetForceHighlight(bool force, HighlightHold source)
    {
        HighlightHold previous = _highlightHolds;

        if (force)
            _highlightHolds |= source;
        else
            _highlightHolds &= ~source;

        if (_highlightHolds == previous) return;

        bool anyHold = _highlightHolds != HighlightHold.None;

        // Awake may not have run yet if something registers this object extremely early.
        if (highlightEffect == null)
            highlightEffect = GetComponent<HighlightEffect>();

        if (highlightEffect != null)
        {
            highlightEffect.enabled = true;
            ApplyHighlightProfile();
            highlightEffect.highlighted = anyHold;
        }

        if (anyHold)
        {
            OnHighlight();
        }
        else
        {
            OnStopHighlight();
        }
    }

    /// <summary>
    /// Loads whichever of <see cref="ForceHighlightProfile"/> / the authored profile matches the
    /// current state: the authored one while hovered (or while nothing holds the highlight), the
    /// hold profile otherwise.
    ///
    /// No-ops unless BOTH profiles exist — with no authored baseline a swap could never be undone,
    /// so an object whose HighlightEffect has no profile assigned is simply left alone rather than
    /// permanently restyled.
    /// </summary>
    private void ApplyHighlightProfile()
    {
        if (highlightEffect == null) return;

        HighlightProfile holdProfile = ForceHighlightProfile;
        if (holdProfile == null || _defaultProfile == null) return;

        bool useHoldProfile = !_hovered && _highlightHolds != HighlightHold.None;
        HighlightProfile desired = useHoldProfile ? holdProfile : _defaultProfile;

        if (desired == _appliedProfile) return;

        _appliedProfile = desired;
        highlightEffect.ProfileLoad(desired);

        // HighlightProfile.Load overwrites effectNameFilter with the profile's own (empty) value,
        // which would silently widen the effect to every child renderer — reassert the authored
        // filter exactly as Awake does.
        if (!string.IsNullOrEmpty(highlightNameFilter))
            highlightEffect.effectNameFilter = highlightNameFilter;
    }

    protected virtual void OnHighlight()
    {
        
    }

    protected virtual void OnStopHighlight()
    {
    }

    public virtual void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        OnInteractWithItem?.Invoke();
    }
}