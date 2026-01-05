using System;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;

public interface IInteractable
{
    void Interact(PlayerInteractionController player);
    
}

[RequireComponent(typeof(HighlightEffect))]
public abstract class Interactable : NetworkBehaviour, IInteractable
{
    HighlightEffect highlightEffect;
    public abstract void Interact(PlayerInteractionController player);

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
}