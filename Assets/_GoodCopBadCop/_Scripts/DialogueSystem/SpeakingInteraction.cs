using System;
using System.Collections;
using System.Collections.Generic;
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

    // Server-authoritative engagement flag, true while any player holds a world-dialogue
    // conversation with this speaker. Read by everyone, written only by the server. Multiple
    // players may be engaged simultaneously — see the "World Dialogue Voting" region below,
    // which resolves conflicting dialogue-option picks across every engaged participant using
    // the same rule ScriptedDialogueRunner uses for interrogation-booth choice nodes.
    private readonly NetworkVariable<bool> _isEngaged =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

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

    // Releases this client's engagement participation if it disconnects mid-conversation, so a
    // vanished player doesn't leave the choice/advance gates waiting on them forever.
    private void OnClientDisconnected(ulong clientId)
    {
        if (_worldParticipants.Remove(clientId))
        {
            _isEngaged.Value = _worldParticipants.Count > 0;
            RecheckWorldGatesAfterParticipantChange();
        }
    }

    /// <summary>
    /// Requests to join this speaker's world-dialogue conversation. Multiple players may join
    /// the same conversation simultaneously — the join is always granted, there is no more
    /// exclusivity lock. <paramref name="onResponse"/> is invoked once the server confirms the
    /// join. Safe to call from any client.
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
        bool isFirstParticipant = _worldParticipants.Count == 0;
        _worldParticipants.Add(senderId);
        _isEngaged.Value = true;

        ClientRpcParams targetParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { senderId } }
        };
        EngagementResponseClientRpc(true, targetParams);

        if (isFirstParticipant)
        {
            // First participant starts the authoritative conversation loop on the server.
            GetComponent<SuspectWorldDialogue>()?.ServerStartConversation();
        }
        else if (_awaitingWorldChoice && _lastShownWorldChoiceTexts != null)
        {
            // Catch a mid-conversation joiner up with the choice panel that's already open,
            // so they can immediately take part in the vote.
            SplitChoiceTexts(_lastShownWorldChoiceTexts, out string c0, out string c1, out string c2);
            ShowWorldChoicesClientRpc(c0, c1, c2, targetParams);
        }
        else if (_awaitingWorldAdvance)
        {
            SetWorldAwaitingAdvanceClientRpc(true, targetParams);
        }
    }

    [ClientRpc]
    private void EngagementResponseClientRpc(bool granted, ClientRpcParams rpcParams = default)
    {
        Action<bool> callback = _pendingEngagementCallback;
        _pendingEngagementCallback = null;
        callback?.Invoke(granted);
    }

    /// <summary>Leaves this speaker's world-dialogue conversation. Safe to call even if not engaged.</summary>
    public void EndEngagement()
    {
        EndEngagementServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void EndEngagementServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (_worldParticipants.Remove(senderId))
        {
            _isEngaged.Value = _worldParticipants.Count > 0;
            RecheckWorldGatesAfterParticipantChange();
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
            SayWorldDialogueClientRpc(dialogue, WorldParticipantIdsSnapshot(), clearHistory, waitForInput);
        }
        else
        {
            SayWorldDialogueServerRpc(dialogue, clearHistory, waitForInput);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void SayWorldDialogueServerRpc(string dialogue, bool clearHistory, bool waitForInput)
    {
        SayWorldDialogueClientRpc(dialogue, WorldParticipantIdsSnapshot(), clearHistory, waitForInput);
    }

    /// <summary>Server-only snapshot of the clients currently engaged in this speaker's world-dialogue conversation.</summary>
    private ulong[] WorldParticipantIdsSnapshot()
    {
        ulong[] ids = new ulong[_worldParticipants.Count];
        _worldParticipants.CopyTo(ids);
        return ids;
    }

    [ClientRpc]
    private void SayWorldDialogueClientRpc(string dialogue, ulong[] participantIds, bool clearHistory,
        bool waitForInput)
    {
        bool isEngagedPlayer = NetworkManager.Singleton != null &&
                               Array.IndexOf(participantIds, NetworkManager.Singleton.LocalClientId) >= 0;

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

    // ===========================================================================================
    // World dialogue — multiplayer participants & choice voting
    // ===========================================================================================
    //
    // Both players may now hold the same world-dialogue conversation (SuspectWorldDialogue) with
    // this speaker at once. When they pick different dialogue options, the winning pick is
    // resolved with the exact same rule ScriptedDialogueRunner uses for interrogation-booth
    // choice nodes: a unanimous pick wins outright, a split pick is decided by a random draw
    // from the submitted options only (never from options nobody picked), and a per-pick timeout
    // keeps the conversation moving if a participant never answers. The same "wait for every
    // participant, or timeout" gate is applied to advancing past the greeting/response lines, so
    // both participants stay in lock-step exactly like a scripted dialogue sequence.

    [Header("World Dialogue Voting")]
    [Tooltip("Seconds after the first participant advances a line or submits a choice before " +
             "the conversation continues automatically without the other participant's input. " +
             "Mirrors ScriptedDialogueRunner's equivalent multi-player timeout.")]
    [SerializeField] private float _worldAdvanceTimeoutSeconds = 1.5f;

    // Server-only: clients currently engaged in a world-dialogue conversation with this speaker.
    private readonly HashSet<ulong> _worldParticipants = new HashSet<ulong>();

    /// <summary>Server-only: true while at least one client is engaged with this speaker.</summary>
    public bool HasWorldParticipants => _worldParticipants.Count > 0;

    // Server-only — line-advance gate (greeting / NPC response lines).
    private readonly HashSet<ulong> _worldAdvanceSet = new HashSet<ulong>();
    private bool _awaitingWorldAdvance;
    private bool _worldAdvanceReady;
    private Coroutine _worldAdvanceTimerCoroutine;
    private Action _onWorldAdvanceOpened;

    // Server-only — choice-vote gate.
    private readonly Dictionary<ulong, int> _worldChoiceSubmissions = new Dictionary<ulong, int>();
    private readonly Dictionary<ulong, string> _worldChoicePlayerNames = new Dictionary<ulong, string>();
    private bool _awaitingWorldChoice;
    private bool _worldChoiceResolved;
    private int _resolvedWorldChoiceIndex;
    private Coroutine _worldChoiceTimerCoroutine;
    private string[] _lastShownWorldChoiceTexts;
    private Action<int> _onWorldChoiceResolved;

    // Client-only: true while the server is waiting for this client to advance the current line.
    private bool _clientAwaitingWorldAdvance;

    /// <summary>True on this client while the server is waiting for it to advance the current world-dialogue line.</summary>
    public bool IsAwaitingWorldAdvance => _clientAwaitingWorldAdvance;

    /// <summary>
    /// Fired locally whenever the server shows the world-dialogue choice panel to this client.
    /// <see cref="SuspectWorldDialogue"/> subscribes to route the player's pick into
    /// <see cref="SubmitWorldChoicePick"/> via <see cref="DialogueChoiceSystem.ShowScriptedChoices"/>.
    /// </summary>
    public event Action<string[]> OnWorldChoicesShown;

    /// <summary>Fired locally once the server has resolved (or timed out) the current choice vote.</summary>
    public event Action OnWorldChoiceFinalized;

    /// <summary>Fired locally whenever <see cref="IsAwaitingWorldAdvance"/> changes.</summary>
    public event Action<bool> OnWorldAdvanceGateChanged;

    private ClientRpcParams WorldParticipantRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new List<ulong>(_worldParticipants) }
        };
    }

    // -------------------------------------------------------------------------
    // Line-advance gate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. Opens the line-advance gate for the current world-dialogue line (greeting or
    /// NPC response) and calls <paramref name="onOpened"/> once every current participant has
    /// advanced past it, or the per-participant timeout expires — mirrors
    /// <c>ScriptedDialogueRunner.SayAndWait</c> / <c>WaitForScriptedAdvance</c>.
    /// </summary>
    public void ServerBeginWorldAdvanceGate(Action onOpened)
    {
        _worldAdvanceSet.Clear();
        _worldAdvanceReady = false;
        _awaitingWorldAdvance = true;
        _onWorldAdvanceOpened = onOpened;
        SetWorldAwaitingAdvanceClientRpc(true, WorldParticipantRpcParams());
    }

    [ClientRpc]
    private void SetWorldAwaitingAdvanceClientRpc(bool awaiting, ClientRpcParams rpcParams = default)
    {
        _clientAwaitingWorldAdvance = awaiting;
        OnWorldAdvanceGateChanged?.Invoke(awaiting);
    }

    /// <summary>
    /// Sent by a participant pressing E/click to advance the current greeting/response line.
    /// After the first submission a countdown begins; the gate opens once every current
    /// participant has submitted or the countdown expires — mirrors
    /// <c>ScriptedDialogueRunner.AdvanceScriptedLineServerRpc</c>.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitWorldAdvanceServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!_awaitingWorldAdvance || _worldAdvanceReady) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        _worldParticipants.Add(senderId);
        _worldAdvanceSet.Add(senderId);

        int required = Mathf.Max(1, _worldParticipants.Count);

        if (_worldAdvanceSet.Count == 1 && _worldAdvanceTimerCoroutine == null && required > 1)
            _worldAdvanceTimerCoroutine = StartCoroutine(WorldAdvanceTimeoutCoroutine());

        if (_worldAdvanceSet.Count >= required)
            OpenWorldAdvanceGate();
    }

    private IEnumerator WorldAdvanceTimeoutCoroutine()
    {
        ShowWorldAdvanceTimerClientRpc(_worldAdvanceTimeoutSeconds, WorldParticipantRpcParams());
        yield return new WaitForSeconds(_worldAdvanceTimeoutSeconds);
        OpenWorldAdvanceGate();
    }

    [ClientRpc]
    private void ShowWorldAdvanceTimerClientRpc(float duration, ClientRpcParams rpcParams = default)
    {
        DialogueAdvanceTimer.Instance?.Show(duration);
    }

    [ClientRpc]
    private void HideWorldAdvanceTimerClientRpc(ClientRpcParams rpcParams = default)
    {
        DialogueAdvanceTimer.Instance?.Hide();
    }

    private void OpenWorldAdvanceGate()
    {
        if (_worldAdvanceReady) return;
        _worldAdvanceReady = true;
        _awaitingWorldAdvance = false;

        if (_worldAdvanceTimerCoroutine != null)
        {
            StopCoroutine(_worldAdvanceTimerCoroutine);
            _worldAdvanceTimerCoroutine = null;
        }

        HideWorldAdvanceTimerClientRpc(WorldParticipantRpcParams());
        SetWorldAwaitingAdvanceClientRpc(false, WorldParticipantRpcParams());

        // Reuse the shared subtitle-advance broadcast so the waiting line is cleared for every
        // participant exactly the same way a normal (non-voted) advance would clear it.
        DialogueManager.Instance.AdvanceDialogueServerRpc();

        Action callback = _onWorldAdvanceOpened;
        _onWorldAdvanceOpened = null;
        callback?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Choice-vote gate
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-only. Shows the world-dialogue choice panel to every current participant and
    /// collects their picks. A unanimous pick wins outright; conflicting picks are resolved by a
    /// random draw from the submitted options only — mirrors <c>ScriptedDialogueRunner.ResolveChoices</c>.
    /// Calls <paramref name="onResolved"/> with the winning index once resolved. Supports up to 3
    /// options (matching <see cref="SuspectWorldDialogue"/>'s authored option limit) — like
    /// <c>ScriptedDialogueRunner.ShowChoicesClientRpc</c>, texts are sent as individual string
    /// parameters rather than a string[] array, since NGO arrays of managed types are not used
    /// elsewhere in this project.
    /// </summary>
    public void ServerBeginWorldChoiceVote(string[] choiceTexts, Action<int> onResolved)
    {
        _worldChoiceSubmissions.Clear();
        _worldChoicePlayerNames.Clear();
        _worldChoiceResolved = false;
        _resolvedWorldChoiceIndex = -1;
        _awaitingWorldChoice = true;
        _lastShownWorldChoiceTexts = choiceTexts;
        _onWorldChoiceResolved = onResolved;
        SplitChoiceTexts(choiceTexts, out string c0, out string c1, out string c2);
        ShowWorldChoicesClientRpc(c0, c1, c2, WorldParticipantRpcParams());
    }

    private static void SplitChoiceTexts(string[] texts, out string choice0, out string choice1, out string choice2)
    {
        choice0 = texts.Length > 0 ? texts[0] : string.Empty;
        choice1 = texts.Length > 1 ? texts[1] : string.Empty;
        choice2 = texts.Length > 2 ? texts[2] : string.Empty;
    }

    [ClientRpc]
    private void ShowWorldChoicesClientRpc(string choice0, string choice1, string choice2, ClientRpcParams rpcParams = default)
    {
        var texts = new List<string> { choice0, choice1 };
        if (!string.IsNullOrEmpty(choice2)) texts.Add(choice2);
        OnWorldChoicesShown?.Invoke(texts.ToArray());
    }

    /// <summary>
    /// Called locally by <see cref="SuspectWorldDialogue"/> once the player clicks a choice
    /// button: highlights the pick immediately (mirrors <c>ScriptedDialogueRunner.OnLocalPlayerPickedChoice</c>)
    /// and submits the vote to the server. The panel stays open until every participant has
    /// picked (or the timeout fires).
    /// </summary>
    public void SubmitWorldChoicePick(int choiceIndex)
    {
        DialogueChoiceSystem.Instance.HighlightChoice(choiceIndex);
        SubmitWorldChoiceServerRpc(choiceIndex, GetLocalPlayerName());
    }

    [ServerRpc(RequireOwnership = false)]
    private void SubmitWorldChoiceServerRpc(int choiceIndex, string playerName, ServerRpcParams rpcParams = default)
    {
        if (_worldChoiceResolved) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (_worldChoiceSubmissions.ContainsKey(senderId)) return; // ignore re-submissions

        _worldParticipants.Add(senderId);
        _worldChoiceSubmissions[senderId] = choiceIndex;
        _worldChoicePlayerNames[senderId] = playerName;

        HighlightWorldChoiceForOthersClientRpc(choiceIndex, senderId);

        int required = Mathf.Max(1, _worldParticipants.Count);

        if (_worldChoiceSubmissions.Count == 1 && required > 1)
            _worldChoiceTimerCoroutine = StartCoroutine(WorldChoiceTimeoutCoroutine());

        if (_worldChoiceSubmissions.Count >= required)
            ResolveWorldChoiceVote();
    }

    /// <summary>Highlights the submitted choice on every client except the sender, who already highlighted locally.</summary>
    [ClientRpc]
    private void HighlightWorldChoiceForOthersClientRpc(int choiceIndex, ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        DialogueChoiceSystem.Instance?.HighlightChoice(choiceIndex);
    }

    private IEnumerator WorldChoiceTimeoutCoroutine()
    {
        ShowWorldAdvanceTimerClientRpc(_worldAdvanceTimeoutSeconds, WorldParticipantRpcParams());
        yield return new WaitForSeconds(_worldAdvanceTimeoutSeconds);
        ResolveWorldChoiceVote();
    }

    private void ResolveWorldChoiceVote()
    {
        if (_worldChoiceResolved) return;
        _worldChoiceResolved = true;
        _awaitingWorldChoice = false;

        if (_worldChoiceTimerCoroutine != null)
        {
            StopCoroutine(_worldChoiceTimerCoroutine);
            _worldChoiceTimerCoroutine = null;
        }

        HideWorldAdvanceTimerClientRpc(WorldParticipantRpcParams());

        var submitted = new List<int>(_worldChoiceSubmissions.Values);

        if (submitted.Count == 0)
        {
            _resolvedWorldChoiceIndex = 0;
            Debug.LogWarning($"[SpeakingInteraction] ResolveWorldChoiceVote on '{speakerName}' called with no submissions. Defaulting to choice 0.");
        }
        else
        {
            bool unanimous = submitted.TrueForAll(v => v == submitted[0]);
            _resolvedWorldChoiceIndex = unanimous
                ? submitted[0]
                : submitted[UnityEngine.Random.Range(0, submitted.Count)];
        }

        // Resolve the winning player's PlayerObject so bystanders overhearing in InWorldSubtitles
        // mode can be shown the echo bubble above the correct head (mirrors
        // ScriptedDialogueRunner's ShowInWorldSubtitleClientRpc winner lookup).
        ulong winnerPlayerNetId = 0;
        ulong winnerClientId = FindWorldChoiceWinnerClientId(_resolvedWorldChoiceIndex);
        if (winnerClientId != ulong.MaxValue &&
            NetworkManager.Singleton.ConnectedClients.TryGetValue(winnerClientId, out var winnerClient) &&
            winnerClient.PlayerObject != null)
        {
            winnerPlayerNetId = winnerClient.PlayerObject.NetworkObjectId;
        }

        // Broadcast to every client, not just current participants, so a nearby non-engaged
        // player "listening in" also sees the pick — mirrors how SayWorldDialogueClientRpc
        // already broadcasts response lines to overhearing bystanders.
        FinalizeWorldChoiceClientRpc(
            WinningChoiceText(_resolvedWorldChoiceIndex),
            FindWorldChoiceWinnerName(_resolvedWorldChoiceIndex),
            WorldParticipantIdsSnapshot(),
            winnerPlayerNetId);

        Action<int> callback = _onWorldChoiceResolved;
        _onWorldChoiceResolved = null;
        callback?.Invoke(_resolvedWorldChoiceIndex);
    }

    /// <summary>
    /// Resolves the winning choice's spoken text from the texts most recently shown via
    /// <see cref="ServerBeginWorldChoiceVote"/>. Returns empty if out of range (e.g. an
    /// unattended timeout with no options at all).
    /// </summary>
    private string WinningChoiceText(int winningIndex)
    {
        if (_lastShownWorldChoiceTexts == null || winningIndex < 0 || winningIndex >= _lastShownWorldChoiceTexts.Length)
            return string.Empty;
        return _lastShownWorldChoiceTexts[winningIndex];
    }

    /// <summary>
    /// Finds the name of the player who submitted <paramref name="winningIndex"/>.
    /// Falls back to a generic label if no submission matches (e.g. timeout with no picks).
    /// Mirrors <c>ScriptedDialogueRunner.FindWinnerName</c>.
    /// </summary>
    private string FindWorldChoiceWinnerName(int winningIndex)
    {
        foreach (var kvp in _worldChoiceSubmissions)
        {
            if (kvp.Value == winningIndex && _worldChoicePlayerNames.TryGetValue(kvp.Key, out string name))
                return name;
        }
        return "Detective";
    }

    /// <summary>
    /// Finds the clientId of the participant who submitted <paramref name="winningIndex"/>.
    /// Returns <see cref="ulong.MaxValue"/> if no submission matches (e.g. an unattended timeout).
    /// Mirrors <c>ScriptedDialogueRunner.FindWinnerClientId</c>.
    /// </summary>
    private ulong FindWorldChoiceWinnerClientId(int winningIndex)
    {
        foreach (var kvp in _worldChoiceSubmissions)
        {
            if (kvp.Value == winningIndex)
                return kvp.Key;
        }
        return ulong.MaxValue;
    }

    /// <summary>
    /// Shows the winning player's spoken line as a caption above the NPC's upcoming response
    /// subtitle. Broadcast to every client: an engaged participant gets the persistent on-screen
    /// echo (mirrors <c>ScriptedDialogueRunner.FinalizeChoiceClientRpc</c>'s use of
    /// <see cref="DialogueManager.ShowChoiceEcho"/>); a nearby non-engaged player "listening in"
    /// instead gets the same overhear treatment <see cref="SayWorldDialogueClientRpc"/> already
    /// gives response lines — a normal auto-dismissing subtitle, or an in-world bubble above
    /// <paramref name="winnerPlayerNetId"/>'s head, depending on
    /// <see cref="GameSettings.WorldDialogueOverhearMode"/>.
    /// </summary>
    [ClientRpc]
    private void FinalizeWorldChoiceClientRpc(string choiceText, string playerName, ulong[] participantIds,
        ulong winnerPlayerNetId, ClientRpcParams rpcParams = default)
    {
        bool isEngagedPlayer = NetworkManager.Singleton != null &&
                                Array.IndexOf(participantIds, NetworkManager.Singleton.LocalClientId) >= 0;

        if (isEngagedPlayer)
        {
            DialogueChoiceSystem.Instance?.ResetChoiceHighlights();
            DialogueChoiceSystem.Instance?.HideChoicePanel();

            if (!string.IsNullOrEmpty(choiceText))
                DialogueManager.Instance?.ShowChoiceEcho(choiceText, playerName, Color.white);
        }
        else if (!string.IsNullOrEmpty(choiceText) && IsLocalPlayerWithinOverhearProximity())
        {
            if (GameSettings.Instance.WorldDialogueOverhearMode == GameSettings.OverhearSubtitleMode.NormalSubtitles)
            {
                // Use the same persistent echo caption as the engaged branch (not a regular
                // SpawnSubtitles call): a regular subtitle would be immediately wiped out by
                // DestroyPreviousSubtitles the instant the NPC's response line spawns below it,
                // exactly the bug ShowChoiceEcho/HideChoiceEcho already exists to avoid.
                DialogueManager.Instance?.ShowChoiceEcho(choiceText, playerName, Color.white);
            }
            else if (winnerPlayerNetId != 0 &&
                     NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(winnerPlayerNetId, out var netObj))
            {
                netObj.GetComponent<InWorldSubtitleAnchor>()?.Subtitle?.ShowLine(choiceText, playerName, Color.cyan);
            }
        }

        OnWorldChoiceFinalized?.Invoke();
    }

    /// <summary>
    /// Re-evaluates the advance/choice gates after <see cref="_worldParticipants"/> shrinks (a
    /// participant left or disconnected). Opens/resolves immediately if the remaining
    /// participants have already all submitted, or if nobody is left to answer at all — mirrors
    /// <c>ScriptedDialogueRunner.RecheckGatesAfterParticipantChange</c>.
    /// </summary>
    private void RecheckWorldGatesAfterParticipantChange()
    {
        if (_worldParticipants.Count == 0)
        {
            if (_awaitingWorldAdvance && !_worldAdvanceReady) OpenWorldAdvanceGate();
            if (_awaitingWorldChoice && !_worldChoiceResolved) ResolveWorldChoiceVote();
            return;
        }

        int required = _worldParticipants.Count;

        if (_awaitingWorldAdvance && !_worldAdvanceReady && _worldAdvanceSet.Count >= required)
            OpenWorldAdvanceGate();
        else if (_awaitingWorldChoice && !_worldChoiceResolved && _worldChoiceSubmissions.Count >= required)
            ResolveWorldChoiceVote();
    }

    /// <summary>Mirrors <c>ScriptedDialogueRunner.GetLocalPlayerName</c>.</summary>
    private string GetLocalPlayerName()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
            return Steamworks.SteamClient.Name;

        return $"Player {NetworkManager.Singleton.LocalClientId}";
    }
}
