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

    [Header("Mutant Voice")]
    [Tooltip("Distortion level applied when mutant voice is active (0 = none, 1 = maximum).")]
    [Range(0f, 1f)]
    [SerializeField] private float _mutantDistortionLevel = 0.35f;

    private bool _isMutantVoiceActive;

    /// <summary>
    /// True once <see cref="SetMutantVoice"/> has been called with <c>true</c>.
    /// Used by DialogueManager to restrict pitch shifting to fully-mutated characters only.
    /// </summary>
    public bool IsMutantVoiceActive => _isMutantVoiceActive;

    private bool _isLaughing;

    /// <summary>
    /// True while this speaker's <see cref="LaughingAnomaly"/> is active. While laughing,
    /// normal dialogue voice clips are suppressed — the character is busy laughing instead
    /// of delivering their line. Driven by <see cref="LaughingAnomaly"/> Activate/Deactivate.
    /// </summary>
    public bool IsLaughing => _isLaughing;

    /// <summary>
    /// Sets whether this speaker is currently in the middle of a laughing fit.
    /// Called by <see cref="LaughingAnomaly"/> when it activates/deactivates.
    /// </summary>
    public void SetLaughing(bool isLaughing)
    {
        _isLaughing = isLaughing;
    }

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

    [Header("Laugh SFX")]
    [Tooltip("Optional laugh sound effect(s). One is chosen at random and played on this " +
             "speaker's AudioSource when a ScriptedDialogueNode/Choice has playLaughSfx enabled.")]
    [SerializeField] private AudioClip[] laughClips;

    /// <summary>
    /// Plays a random laugh clip on this speaker's AudioSource. Safe to call on any client —
    /// invoked locally by <see cref="ScriptedDialogueRunner"/>'s laugh ClientRpc. No-ops if no
    /// laugh clips or AudioSource are assigned.
    /// </summary>
    public void PlayLaugh()
    {
        if (audioSource == null || laughClips == null || laughClips.Length == 0) return;

        AudioClip clip = laughClips[Random.Range(0, laughClips.Length)];
        if (clip != null)
            audioSource.PlayOneShot(clip);
    }

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
        if (!_isLaughing && clips != null && clips.Length > 0 && audioSource != null)
        {
            DialogueManager.Instance.PlayDialogueAudio(dialogue, clips, audioSource, isMutant: _isMutantVoiceActive);
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

    /// <summary>
    /// Applies or removes the mutant voice effect on the AudioSource.
    /// When enabled, lowers pitch and adds an AudioDistortionFilter for a distorted, deeper tone.
    /// Safe to call on any client.
    /// </summary>
    public void SetMutantVoice(bool isMutant)
    {
        if (audioSource == null) return;

        _isMutantVoiceActive = isMutant;

        AudioDistortionFilter distortion = audioSource.gameObject.GetComponent<AudioDistortionFilter>();

        if (isMutant)
        {
            if (distortion == null)
                distortion = audioSource.gameObject.AddComponent<AudioDistortionFilter>();

            distortion.distortionLevel = _mutantDistortionLevel;
            distortion.enabled = true;
        }
        else if (distortion != null)
        {
            distortion.enabled = false;
        }
    }
}
