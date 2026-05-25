using UnityEngine;

public class Guard : Interactable
{
    [SerializeField] private string[] dialogueBlurbs;
    [SerializeField] private SpeakingInteraction speaking;

    protected override void Awake()
    {
        base.Awake();
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
