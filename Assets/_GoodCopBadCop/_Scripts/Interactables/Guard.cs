using Unity.Netcode;
using UnityEngine;

public class Guard : Interactable
{
    private const string InteractLabel = "Talk";

    [SerializeField] private string guardName = "Guard";
    [SerializeField] private string[] dialogueBlurbs;
    [SerializeField] private AudioClip[] voiceAudioClips;
    [SerializeField] private AudioSource audioSource;

    protected override void Awake()
    {
        base.Awake();
        interactText = InteractLabel;
    }

    /// <summary>
    /// Makes the guard say a blurb and play voice audio via the dialogue manager on all clients.
    /// Ignored while the guard is still finishing a previous statement.
    /// </summary>
    public override void Interact(PlayerInteractionController player)
    {
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsSpeaking)
            return;

        base.Interact(player);
        string dialogueBlurb = dialogueBlurbs[UnityEngine.Random.Range(0, dialogueBlurbs.Length)];

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
