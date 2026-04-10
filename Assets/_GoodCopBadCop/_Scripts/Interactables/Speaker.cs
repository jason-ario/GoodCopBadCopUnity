using UnityEngine;

public class Speaker : Interactable
{
    [SerializeField] private string[] choices;
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        DialogueManager.Instance.InitiateChoices(transform, choices);
    }
}
