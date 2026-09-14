using System;
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

        AudioClip clip = laughClips[UnityEngine.Random.Range(0, laughClips.Length)];
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

    // Server-authoritative lock so only one player at a time can hold a world-dialogue
    // conversation with this speaker. Read by everyone, written only by the server.
    private readonly NetworkVariable<bool> _isEngaged =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _engagedClientId =
        new NetworkVariable<ulong>(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Action<bool> _pendingEngagementCallback;

    /// <summary>True while any player currently holds a world-dialogue conversation with this speaker.</summary>
    public bool IsEngaged => _isEngaged.Value;

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // Releases the engagement lock if the player who was holding it disconnects mid-conversation,
    // so this speaker doesn't stay permanently unavailable to everyone else.
    private void OnClientDisconnected(ulong clientId)
    {
        if (_isEngaged.Value && _engagedClientId.Value == clientId)
        {
            _isEngaged.Value = false;
            _engagedClientId.Value = ulong.MaxValue;
        }
    }

    /// <summary>
    /// Requests exclusive engagement with this speaker for a world-dialogue conversation.
    /// Invokes <paramref name="onResponse"/> with true if granted, or false if another player
    /// already has this speaker engaged. Safe to call from any client.
    /// </summary>
    public void RequestBeginEngagement(Action<bool> onResponse)
    {
        _pendingEngagementCallback = onResponse;
        RequestBeginEngagementServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestBeginEngagementServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        bool granted = !_isEngaged.Value;

        if (granted)
        {
            _isEngaged.Value = true;
            _engagedClientId.Value = senderId;
        }

        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
        };
        EngagementResponseClientRpc(granted, targetParams);
    }

    [ClientRpc]
    private void EngagementResponseClientRpc(bool granted, ClientRpcParams rpcParams = default)
    {
        Action<bool> callback = _pendingEngagementCallback;
        _pendingEngagementCallback = null;
        callback?.Invoke(granted);
    }

    /// <summary>Releases this speaker's engagement lock. Safe to call even if not currently engaged.</summary>
    public void EndEngagement()
    {
        EndEngagementServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndEngagementServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (_isEngaged.Value && _engagedClientId.Value == senderId)
        {
            _isEngaged.Value = false;
            _engagedClientId.Value = ulong.MaxValue;
        }
    }

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
    /// Plays a direct world-dialogue line. The player who initiated the conversation receives the
    /// regular dialogue subtitle. Every other (non-engaged) client "overhears" the same line only
    /// while within <see cref="GameSettings.OverhearSubtitleProximity"/> of the speaker, according
    /// to <see cref="GameSettings.WorldDialogueOverhearMode"/>: either as a floating bubble over
    /// this speaker (InWorldSubtitles), or as the regular bottom-of-screen subtitle
    /// (NormalSubtitles). Outside that proximity, non-engaged clients see nothing.
    /// </summary>
    public void SayWorldDialogue(string dialogue, bool clearHistory = false, bool waitForInput = false)
    {
        if (IsServer)
        {
            SayWorldDialogueClientRpc(dialogue, NetworkManager.Singleton.LocalClientId, clearHistory, waitForInput);
        }
        else
        {
            SayWorldDialogueServerRpc(dialogue, clearHistory, waitForInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayWorldDialogueServerRpc(string dialogue, bool clearHistory, bool waitForInput,
        ServerRpcParams rpcParams = default)
    {
        SayWorldDialogueClientRpc(dialogue, rpcParams.Receive.SenderClientId, clearHistory, waitForInput);
    }

    [ClientRpc]
    private void SayWorldDialogueClientRpc(string dialogue, ulong engagedClientId, bool clearHistory,
        bool waitForInput)
    {
        bool isEngagedPlayer = NetworkManager.Singleton != null &&
                               NetworkManager.Singleton.LocalClientId == engagedClientId;

        if (isEngagedPlayer)
        {
            DialogueManager.Instance?.SpawnSubtitles(dialogue, speakerName, Color.white, false, clearHistory,
                waitForInput);
        }
        else
        {
            if (clearHistory)
                DialogueManager.Instance?.ClearHistory();

            if (IsLocalPlayerWithinOverhearProximity())
            {
                if (GameSettings.Instance.WorldDialogueOverhearMode == GameSettings.OverhearSubtitleMode.NormalSubtitles)
                {
                    // waitForInput is intentionally ignored here: the overhearing client has no way to
                    // advance this line themselves, so let it auto-dismiss on its own timer instead.
                    DialogueManager.Instance?.SpawnSubtitles(dialogue, speakerName, Color.white, false, clearHistory,
                        waitForInput: false);
                }
                else
                {
                    GetComponent<InWorldSubtitleAnchor>()?.Subtitle?.ShowLine(dialogue, speakerName, Color.white);
                }
            }
        }

        AudioClip[] clips = VoiceAudioClips;
        if (!_isLaughing && clips != null && clips.Length > 0 && audioSource != null)
        {
            DialogueManager.Instance?.PlayDialogueAudio(dialogue, clips, audioSource, isMutant: _isMutantVoiceActive);
        }
    }

    /// <summary>
    /// True when the local player is within <see cref="GameSettings.OverhearSubtitleProximity"/> of
    /// this speaker. Used to decide whether an overheard world-dialogue line is shown at all, and if
    /// so, whether it uses the bottom-of-screen subtitle (NormalSubtitles mode) or the in-world
    /// bubble (InWorldSubtitles mode).
    /// </summary>
    private bool IsLocalPlayerWithinOverhearProximity()
    {
        Transform localPlayer = PlayerInstance.Instance != null ? PlayerInstance.Instance.transform : null;
        if (localPlayer == null) return false;

        float maxDistance = GameSettings.Instance.OverhearSubtitleProximity;
        return (localPlayer.position - transform.position).sqrMagnitude <= maxDistance * maxDistance;
    }

    /// <summary>Hides this speaker's floating world-dialogue subtitle on every connected client.</summary>
    public void HideWorldDialogueSubtitle()
    {
        if (IsServer)
        {
            HideWorldDialogueSubtitleClientRpc();
        }
        else
        {
            HideWorldDialogueSubtitleServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void HideWorldDialogueSubtitleServerRpc()
    {
        HideWorldDialogueSubtitleClientRpc();
    }

    [ClientRpc]
    private void HideWorldDialogueSubtitleClientRpc()
    {
        GetComponent<InWorldSubtitleAnchor>()?.Subtitle?.Hide();
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
