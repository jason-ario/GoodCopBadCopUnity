using System;
using HighlightPlus;
using UnityEngine;

public interface IInteractable
{
    void Interact(PlayerInteractionController player);
    
}

[RequireComponent(typeof(HighlightEffect))]
public abstract class Interactable : MonoBehaviour, IInteractable
{
    HighlightEffect highlightEffect;
    public abstract void Interact(PlayerInteractionController player);

    private void Awake()
    {
        highlightEffect = GetComponent<HighlightEffect>();
        highlightEffect.enabled = false;
        highlightEffect.highlighted = true;
        highlightEffect.ProfileLoad(highlightEffect.profile);
    }

    public void Highlight(bool highlight)
    {
        highlightEffect.enabled = highlight;
    }
}