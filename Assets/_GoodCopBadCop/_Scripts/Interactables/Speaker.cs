using UnityEngine;

public class Speaker : Interactable
{
    [SerializeField] private string[] choices;
    protected override string DefaultInteractVerb => "Talk";

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        DialogueManager.Instance.InitiateChoices(transform, choices);
    }
}
