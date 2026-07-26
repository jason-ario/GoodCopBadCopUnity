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
    /// When true, the highlight is held on regardless of hover state — normal
    /// hover-driven <see cref="Highlight"/>(false) calls from
    /// <see cref="PlayerInteractionController"/> are ignored while this is set.
    /// Used for tutorial call-outs (e.g. <see cref="TakeOutTrashTask.HighlightAllItemsForTutorial"/>)
    /// where an object must stay highlighted until a specific task event clears it,
    /// not just until the player's reticle looks away.
    /// </summary>
    private bool _forceHighlighted;

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
        // While force-highlighted (tutorial call-out), ignore hover-driven attempts to
        // turn the highlight off — it should only clear via SetForceHighlight(false).
        if (_forceHighlighted && !highlight) return;

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
    public void SetForceHighlight(bool force)
    {
        _forceHighlighted = force;
        highlightEffect.highlighted = force;

        if (force)
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

    public virtual void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        OnInteractWithItem?.Invoke();
    }
}