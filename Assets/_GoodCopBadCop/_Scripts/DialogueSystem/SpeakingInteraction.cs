using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Reusable component that gives any character the ability to speak through the dialogue system.
/// Encapsulates the speaker's name, voice clips, audio source, and optional player-facing
/// dialogue choices. Attach alongside any character that needs to talk.
/// </summary>
public class SpeakingInteraction : NetworkBehaviour
{
    [Header("Speaker Identity")]
    [SerializeField] private string speakerName = "Character";
    public string SpeakerName => speakerName;

    [Header("Voice")]
    [SerializeField] private AudioClip[] voiceAudioClips;
    [SerializeField] private AudioSource audioSource;

    /// <summary>
    /// Returns the voice clips for this speaker. When a <see cref="SuspectData"/> asset is
    /// assigned and it contains clips, those take priority over the local serialized array —
    /// so per-suspect audio is authored entirely on the SuspectData, not here.
    /// </summary>
    public AudioClip[] VoiceAudioClips =>
        suspectData != null && suspectData.voiceAudioClips != null && suspectData.voiceAudioClips.Length > 0
            ? suspectData.voiceAudioClips
            : voiceAudioClips;

    public AudioSource AudioSource => audioSource;

    [Header("Dialogue Choices")]
    [Tooltip("Populated from SuspectData at runtime when assigned. Used as a fallback for non-suspect speakers (e.g. Guard).")]
    [SerializeField] private string[] dialogueChoices;
    [Tooltip("When assigned, questions are sourced from questionResponses on this asset instead of the dialogueChoices array above.")]
    [SerializeField] private SuspectData suspectData;

    [Header("Look Target")]
    [SerializeField] private Transform lookTarget;
    public Transform LookTarget => lookTarget;

    /// <summary>
    /// Broadcasts a line of dialogue to all clients via the dialogue manager.
    /// Safe to call from either server or client.
    /// </summary>
    public void Say(string dialogue, bool clearHistory = false, bool waitForInput = false)
    {
        if (IsServer)
        {
            SayClientRpc(dialogue, clearHistory, waitForInput);
        }
        else
        {
            SayServerRpc(dialogue, clearHistory, waitForInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayServerRpc(string dialogue, bool clearHistory, bool waitForInput)
    {
        SayClientRpc(dialogue, clearHistory, waitForInput);
    }

    [ClientRpc]
    private void SayClientRpc(string dialogue, bool clearHistory, bool waitForInput)
    {
        DialogueManager.Instance.SpawnSubtitles(dialogue, speakerName, Color.white, false, clearHistory, waitForInput);

        AudioClip[] clips = VoiceAudioClips;
        if (clips != null && clips.Length > 0 && audioSource != null)
        {
            DialogueManager.Instance.PlayDialogueAudio(dialogue, clips, audioSource);
        }
    }

    /// <summary>
    /// Opens the player-facing dialogue choice UI. When a SuspectData asset is assigned,
    /// question text is sourced from its questionResponses array. Falls back to the
    /// dialogueChoices Inspector field for non-suspect speakers such as Guard.
    /// </summary>
    public void InitiateChoices()
    {
        string[] choices = ResolveChoices();

        if (choices == null || choices.Length == 0)
        {
            Debug.LogWarning($"SpeakingInteraction on '{speakerName}': no dialogue choices configured.");
            return;
        }

        DialogueManager.Instance.InitiateChoices(lookTarget, choices);
    }

    private string[] ResolveChoices()
    {
        if (suspectData != null &&
            suspectData.questionResponses != null &&
            suspectData.questionResponses.Length > 0)
        {
            string[] questions = new string[suspectData.questionResponses.Length];
            for (int i = 0; i < questions.Length; i++)
                questions[i] = suspectData.questionResponses[i].question;
            return questions;
        }

        return dialogueChoices;
    }
}
