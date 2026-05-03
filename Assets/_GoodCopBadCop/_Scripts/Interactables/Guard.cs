using Unity.Netcode;
using UnityEngine;

public class Guard : Interactable
{
    [SerializeField] private string guardName = "Guard";
    [SerializeField] private string dialogueBlurb = "Move along now.";
    [SerializeField] private AudioClip[] voiceAudioClips;
    [SerializeField] private AudioSource audioSource;

    /// <summary>
    /// Makes the guard say a blurb and play voice audio via the dialogue manager on all clients.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsServer)
        {
            ShowDialogueClientRpc(dialogueBlurb, guardName);
        }
        else
        {
            ShowDialogueServerRpc(dialogueBlurb, guardName);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void ShowDialogueServerRpc(string dialogue, string speakerName)
    {
        ShowDialogueClientRpc(dialogue, speakerName);
    }

    [ClientRpc]
    private void ShowDialogueClientRpc(string dialogue, string speakerName)
    {
        DialogueManager.Instance.SpawnSubtitles(dialogue, speakerName, Color.white);

        if (voiceAudioClips != null && voiceAudioClips.Length > 0 && audioSource != null)
        {
            DialogueManager.Instance.PlayDialogueAudio(dialogue, voiceAudioClips, audioSource);
        }
    }
}
