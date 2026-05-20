using UnityEngine;

public class Guard : Interactable
{
    private const string InteractLabel = "Talk";

    [SerializeField] private string[] dialogueBlurbs;
    [SerializeField] private SpeakingInteraction speaking;

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractLabel;
    }

    /// <summary>
    /// Picks a random blurb and delegates speaking to the SpeakingInteraction component.
    /// Ignored while the guard is still finishing a previous statement.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking)
            return;

        base.Interact(player);

        string blurb = dialogueBlurbs[UnityEngine.Random.Range(0, dialogueBlurbs.Length)];
        speaking.Say(blurb);
    }
}
