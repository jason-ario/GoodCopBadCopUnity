using UnityEngine;

public class Guard : Interactable
{
    [SerializeField] private string guardName = "Guard";
    [SerializeField] private string dialogueBlurb = "Move along now.";
    [SerializeField] private AudioClip[] voiceAudioClips;
    [SerializeField] private AudioSource audioSource;

    /// <summary>
    /// Makes the guard say a blurb and play voice audio via the dialogue manager.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        DialogueManager.Instance.SpawnSubtitles(dialogueBlurb, guardName, Color.white);

        if (voiceAudioClips != null && voiceAudioClips.Length > 0 && audioSource != null)
        {
            DialogueManager.Instance.PlayDialogueAudio(dialogueBlurb, voiceAudioClips, audioSource);
        }
    }
}
