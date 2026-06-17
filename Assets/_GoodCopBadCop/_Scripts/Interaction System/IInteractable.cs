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

[RequireComponent(typeof(HighlightEffect))]
public abstract class Interactable : NetworkBehaviour, IInteractable
{
    HighlightEffect highlightEffect;
    public PickableItemData[] itemsThatCanInteractWith;
    public UnityAction OnInteract;
    public UnityAction OnInteractWithItem;
    public string interactText;

    /// <summary>
    /// Only highlight children whose names contain this string.
    /// Leave empty to disable highlighting entirely.
    /// </summary>
    [Tooltip("Only highlight children whose names contain this string. Leave empty to disable highlighting entirely.")]
    [SerializeField] private string highlightNameFilter;

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
        highlightEffect.enabled = false;
        highlightEffect.highlighted = true;
        highlightEffect.ProfileLoad(highlightEffect.profile);

        if (!string.IsNullOrEmpty(highlightNameFilter))
            highlightEffect.effectNameFilter = highlightNameFilter;
    }

    public void Highlight(bool highlight)
    {
        highlightEffect.enabled = highlight;

        if (highlight)
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