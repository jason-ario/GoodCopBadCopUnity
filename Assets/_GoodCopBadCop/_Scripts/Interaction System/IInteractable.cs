using System;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public interface IInteractable
{
    void Interact(PlayerInteractionController player);
    
}

[RequireComponent(typeof(HighlightEffect))]
public abstract class Interactable : NetworkBehaviour, IInteractable
{
    HighlightEffect highlightEffect;
    public PickableItemData[] itemsThatCanInteractWith;
    public UnityAction OnInteract;
    public UnityAction OnInteractWithItem;


    public virtual void Interact(PlayerInteractionController player)
    {
        OnInteract?.Invoke();
    }

    protected virtual void Awake()
    {
        highlightEffect = GetComponent<HighlightEffect>();
        highlightEffect.enabled = false;
        highlightEffect.highlighted = true;
        highlightEffect.ProfileLoad(highlightEffect.profile);
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

    public virtual void InteractWithItem(PlayerInteractionController playerInteractionController, PickableItemData itemData)
    {
        OnInteractWithItem?.Invoke();
    }
}