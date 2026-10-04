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
/// Pressing Interact on one with empty hands calls Interact(), just like a PickableObject.
/// </summary>
public interface IPickupSlot { }

// ── Input convention ────────────────────────────────────────────────────────────────────────
// Every world-object interaction (pick up, open, pull, sit, extract…) goes through the Interact
// key (E / gamepad West), whether or not the player is holding an item:
//   • Tap   → Interact()
//   • Hold  → InteractHold(), only when GetHoldInteractVerb() returns a verb. The reticle fills a
//             ring over PlayerInteractionController.holdInteractDuration and shows "Hold E to <verb>".
//             For these objects, Interact() fires on release (a tap) instead of on press.
// LMB / RT is reserved for using the held item — including using it ON a target via
// InteractWithItem() (stamp on paper, key in lock, bag on junk…). It never triggers Interact().
// Inside cursor-driven diegetic views (PC, panels, lockers…) LMB still clicks UI as before.

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

    /// <summary>
    /// Persistent findability glow for a package that is part of an active mail delivery.
    /// Cleared only when that package is correctly sorted or despawned.
    /// </summary>
    MailDelivery = 1 << 2,

    /// <summary>
    /// Held automatically by <see cref="TutorialMarker"/> for as long as a tutorial arrow points at
    /// this object (design rule: every arrowed interactable is also highlighted). Like every other
    /// hold, it renders with the object's authored highlight. Never set this manually — go through the arrow.
    /// </summary>
    TutorialArrow = 1 << 3,
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
    /// When true, the reticle shows <see cref="interactText"/> next to the Interact key icon while
    /// this object is targeted (e.g. <see cref="Chair"/>: "[E] Sit"). When false only the key icon shows.
    /// Ignored while a hold action is available — the "Hold E to …" prompt replaces it.
    /// </summary>
    public virtual bool ShowInteractHint => false;

    /// <summary>
    /// Whether pressing Interact on this object does anything for <paramref name="player"/> right now.
    /// Only drives the reticle's key icon (so it isn't offered when it would do nothing, e.g. a pickup
    /// while the player's hands are full); the press itself is still forwarded to <see cref="Interact"/>.
    /// </summary>
    public virtual bool ShowsInteractPrompt(PlayerInteractionController player) => true;

    /// <summary>
    /// Only highlight children whose names contain this string.
    /// Leave empty to disable highlighting entirely.
    /// </summary>
    [Tooltip("Only highlight children whose names contain this string. Leave empty to disable highlighting entirely.")]
    [SerializeField] private string highlightNameFilter;

    [Header("Persistent Highlight Range")]
    [Tooltip("Persistent (hold) highlights — tutorial, arrow, junk, mail — are only visible while the camera is within " +
             "this many meters. They fade in on approach and fade out beyond it. 0 = unlimited. Hover is unaffected.")]
    [SerializeField, Min(0f)] private float holdHighlightVisibleRange = 50f;
    [Tooltip("Seconds to fade a persistent highlight in / out when crossing its visible range.")]
    [SerializeField, Min(0f)] private float holdHighlightFadeDuration = 0.5f;

    [Header("Persistent Highlight Pulse")]
    [Tooltip("Full pulse cycles per second for persistent (objective) highlights. 0 = no pulse (steady). Hover never pulses.")]
    [SerializeField, Min(0f)] private float holdHighlightPulseSpeed = 0.6f;
    [Tooltip("Outline opacity at the faintest point of the pulse (1 = no visible pulse). Thickness and color never change.")]
    [SerializeField, Range(0.05f, 1f)] private float holdHighlightPulseMinOpacity = 0.4f;

    /// <summary>True while the pulse has moved HighlightEffect.outlineOpacity off 1.</summary>
    private bool _pulseApplied;

    /// <summary>Extra distance past the range before an in-range glow fades out, so it doesn't flicker at the boundary.</summary>
    private const float HoldRangeHysteresis = 1f;

    /// <summary>
    /// Whether the local camera is within <see cref="holdHighlightVisibleRange"/>. Kept up to date by
    /// <see cref="HoldHighlightRangeDriver"/> only while a hold is claimed.
    /// </summary>
    private bool _holdInRange = true;

    // Authored HighlightEffect fade durations, restored for hover and hold changes so only
    // range crossings use the range fade.
    private bool _authoredFadeCached;
    private float _authoredFadeIn;
    private float _authoredFadeOut;

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
    /// While true, every hold is kept (not cleared) but not rendered — e.g. a
    /// <see cref="PickableObject"/> being carried by any player. Clearing it re-shows the glow if
    /// any hold is still claimed, so "still required by the task" is decided by the hold owners.
    /// </summary>
    private bool _holdHighlightSuppressed;

    /// <summary>True while a hold is claimed AND not suppressed — the hold owns this object's highlight state.</summary>
    private bool IsHoldHighlightActive => _highlightHolds != HighlightHold.None && !_holdHighlightSuppressed;

    /// <summary>True while the hold is active AND the camera is in range, i.e. the persistent glow is visible.</summary>
    private bool IsHoldHighlightVisible => IsHoldHighlightActive && _holdInRange;

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

    /// <summary>
    /// When true, <see cref="PlayerInteractionController"/>'s interaction ray ignores every collider
    /// belonging to this object (solid or trigger) and continues to whatever is behind it. Use for
    /// objects that must never be targeted or highlighted themselves and that sit inside another
    /// interactable (e.g. <see cref="InkStampPickup"/> resting in its <see cref="InkStamp"/> holder).
    /// Independent of collider enabled state, so network collider toggles cannot re-expose it.
    /// </summary>
    public virtual bool PassesInteractionRayThrough => false;

    public virtual void Interact(PlayerInteractionController player)
    {
        OnInteract?.Invoke();
    }

    /// <summary>
    /// Verb for this object's secondary "hold Interact" action (e.g. "open", "arm", "take out"),
    /// or null when it has none for <paramref name="player"/> right now. Shown as "Hold E to {verb}".
    /// Returning non-null makes a quick tap call <see cref="Interact"/> on release, and holding for
    /// <see cref="PlayerInteractionController.holdInteractDuration"/> call <see cref="InteractHold"/>.
    /// </summary>
    public virtual string GetHoldInteractVerb(PlayerInteractionController player) => null;

    /// <summary>
    /// Secondary action, fired when the player holds Interact for the full hold duration while
    /// <see cref="GetHoldInteractVerb"/> returns a verb. See the input convention at the top of this file.
    /// </summary>
    public virtual void InteractHold(PlayerInteractionController player) { }

    protected virtual void Awake()
    {
        highlightEffect = GetComponent<HighlightEffect>();
        // Every hover and every hold (tutorial, junk, mail, arrow) uses this one authored highlight —
        // there is no per-source profile swapping. Deliberately do NOT call ProfileLoad here: it
        // copies every field from the shared HighlightProfile asset onto this component, clobbering
        // any per-object tweaks made directly on this HighlightEffect in the Inspector/prefab.
        // Force the component enabled so that HighlightEffect.Start() always runs and
        // SetupMaterial() builds the renderer list (rms). If a prefab was saved with
        // the component disabled, Start() never fires, leaving rms null and causing the
        // first hover to silently fail to render. Visibility is gated by 'highlighted'.
        highlightEffect.enabled = true;
        highlightEffect.highlighted = false;
        CacheAuthoredFade();


        if (!string.IsNullOrEmpty(highlightNameFilter))
            highlightEffect.effectNameFilter = highlightNameFilter;
    }

    public virtual void Highlight(bool highlight)
    {
        // While any hold is active (tutorial call-out, pickup affordance), ignore hover-driven
        // attempts to turn the highlight off — it should only clear via SetForceHighlight(false).
        // Checked against the active (not range-visible) state so hover can't cut a range fade short;
        // the range gate owns visibility while a hold is claimed.
        if (IsHoldHighlightActive && !highlight)
            return;

        // Some subclasses (e.g. WorldPurchaseActionInteractable / WorldShopItemInteractable)
        // share this HighlightEffect with a ShopItem component, which drives its own
        // hover-highlight system by toggling 'enabled' directly (SetHighlightBlocked).
        // If that leaves 'enabled' false (e.g. after the purchase popup closes), our
        // 'highlighted'-driven visibility below would silently never render. Reassert
        // 'enabled' every call so this class's invariant — always enabled, visibility
        // solely via 'highlighted' — holds no matter what else touched the component.
        highlightEffect.enabled = true;

        // Hover is instant: use the authored fade, not the range fade.
        ApplyEffectFade(false);

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
        bool wasVisible = IsHoldHighlightVisible;
        bool hadHold = _highlightHolds != HighlightHold.None;

        if (force)
            _highlightHolds |= source;
        else
            _highlightHolds &= ~source;

        bool hasHold = _highlightHolds != HighlightHold.None;
        if (hasHold && !hadHold)
        {
            // Resolve range up front so a far-away claim never flashes on for a frame.
            _holdInRange = ComputeHoldInRange(true);
            HoldHighlightRangeDriver.Track(this);
        }
        else if (!hasHold && hadHold)
        {
            HoldHighlightRangeDriver.Untrack(this);
            RestorePulseOpacity();
        }

        if (IsHoldHighlightVisible == wasVisible) return;
        ApplyHoldHighlightVisual(false);
    }

    /// <summary>
    /// Called each frame by <see cref="HoldHighlightRangeDriver"/> while a hold is claimed. Fades the
    /// persistent glow in when <paramref name="viewerPosition"/> enters <see cref="holdHighlightVisibleRange"/>
    /// and out when it leaves, mirroring <see cref="TutorialMarker"/>'s visible-range behaviour.
    /// </summary>
    public void UpdateHoldHighlightRange(Vector3 viewerPosition)
    {
        bool inRange = IsWithinHoldRange(viewerPosition, _holdInRange);
        if (inRange == _holdInRange) return;

        bool wasVisible = IsHoldHighlightVisible;
        _holdInRange = inRange;

        if (IsHoldHighlightVisible == wasVisible) return;
        ApplyHoldHighlightVisual(true);
    }

    /// <summary>
    /// Called each frame by <see cref="HoldHighlightRangeDriver"/> while a hold is claimed. Gently fades the
    /// outline's opacity between fully opaque and <see cref="holdHighlightPulseMinOpacity"/> so objective
    /// call-outs read differently from the steady hover highlight. Drives
    /// <c>HighlightEffect.outlineOpacity</c> (a project addition to HighlightPlus), which is applied after
    /// the outline is composed — width and color stay exactly as authored. All objects share one phase
    /// (<paramref name="time"/>) so multiple objectives pulse together.
    /// </summary>
    public void UpdateHoldHighlightPulse(float time)
    {
        if (highlightEffect == null) return;

        if (!IsHoldHighlightActive || holdHighlightPulseSpeed <= 0f)
        {
            RestorePulseOpacity();
            return;
        }

        float wave = 0.5f + 0.5f * Mathf.Sin(time * holdHighlightPulseSpeed * Mathf.PI * 2f);
        highlightEffect.outlineOpacity = Mathf.Lerp(holdHighlightPulseMinOpacity, 1f, wave);
        _pulseApplied = true;
    }

    /// <summary>Returns the outline to full opacity so hover renders steady.</summary>
    private void RestorePulseOpacity()
    {
        if (!_pulseApplied || highlightEffect == null) return;

        highlightEffect.outlineOpacity = 1f;
        _pulseApplied = false;
    }

    private bool ComputeHoldInRange(bool fallback)
    {
        Camera cam = Camera.main;
        return cam == null ? fallback : IsWithinHoldRange(cam.transform.position, false);
    }

    private bool IsWithinHoldRange(Vector3 viewerPosition, bool currentlyInRange)
    {
        if (holdHighlightVisibleRange <= 0f) return true;

        float range = holdHighlightVisibleRange + (currentlyInRange ? HoldRangeHysteresis : 0f);
        return (viewerPosition - transform.position).sqrMagnitude <= range * range;
    }

    private void CacheAuthoredFade()
    {
        if (_authoredFadeCached || highlightEffect == null) return;

        _authoredFadeIn = highlightEffect.fadeInDuration;
        _authoredFadeOut = highlightEffect.fadeOutDuration;
        _authoredFadeCached = true;
    }

    /// <summary>
    /// Sets the HighlightEffect's fade durations right before a <c>highlighted</c> change: the range
    /// fade for distance crossings, the authored values otherwise. Must always be followed by setting
    /// <c>highlighted</c> — HighlightPlus reads these live while a fade is in progress.
    /// </summary>
    private void ApplyEffectFade(bool rangeFade)
    {
        CacheAuthoredFade();

        highlightEffect.fadeInDuration = rangeFade ? holdHighlightFadeDuration : _authoredFadeIn;
        highlightEffect.fadeOutDuration = rangeFade ? holdHighlightFadeDuration : _authoredFadeOut;
    }

    /// <summary>
    /// Hides (or re-shows) the persistent hold glow without releasing any hold. Used by
    /// <see cref="PickableObject"/> while it is carried: picking it up un-highlights it for every
    /// player, and dropping it re-highlights it only if some system still holds the highlight
    /// (e.g. a mail package that is still unsorted).
    /// </summary>
    protected void SetHoldHighlightSuppressed(bool suppressed)
    {
        if (_holdHighlightSuppressed == suppressed) return;

        bool wasVisible = IsHoldHighlightVisible;
        _holdHighlightSuppressed = suppressed;

        if (IsHoldHighlightVisible == wasVisible) return;
        ApplyHoldHighlightVisual(false);
    }

    private void ApplyHoldHighlightVisual(bool rangeFade)
    {
        bool anyHold = IsHoldHighlightVisible;

        // Awake may not have run yet if something registers this object extremely early.
        if (highlightEffect == null)
            highlightEffect = GetComponent<HighlightEffect>();

        if (highlightEffect != null)
        {
            highlightEffect.enabled = true;
            ApplyEffectFade(rangeFade);
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

    protected virtual void OnHighlight()
    {
        
    }

    protected virtual void OnStopHighlight()
    {
    }

    /// <summary>
    /// Whether a held item may be used on this interactable (routes LMB to
    /// <see cref="InteractWithItem"/> and shows the button tooltip). Defaults to the
    /// <see cref="itemsThatCanInteractWith"/> list; override to accept items by type/state.
    /// </summary>
    public virtual bool CanInteractWithItem(PickableObject item)
    {
        return item != null
            && itemsThatCanInteractWith != null
            && Array.IndexOf(itemsThatCanInteractWith, item.ItemData) >= 0;
    }

    public virtual void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        OnInteractWithItem?.Invoke();
    }
}