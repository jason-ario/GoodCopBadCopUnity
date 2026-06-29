using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Drives a <see cref="ScriptedDialogue"/> sequence end-to-end, synchronising subtitle
/// display, player-choice UI, and camera state across all connected clients.
///
/// Call <see cref="PlayDialogue"/> on the server to start a sequence. The runner:
/// <list type="bullet">
///   <item>Locks the booth player's movement and activates the suspect camera.</item>
///   <item>Plays each node in order using <see cref="DialogueManager"/>.</item>
///   <item>For Monologue nodes: waits for the player to press E or left-click to advance.</item>
///   <item>For Choice nodes: shows two buttons, waits for a pick, then plays the NPC reply.</item>
///   <item>Restores all player state when the last node finishes.</item>
/// </list>
///
/// Requires a networked scene object that has this component attached. The singleton
/// <see cref="Instance"/> is resolved automatically in <c>Awake</c>.
/// </summary>
public class ScriptedDialogueRunner : NetworkBehaviour
{
    public static ScriptedDialogueRunner Instance { get; private set; }

    // Set true while coroutines are waiting for player E / left-click input so that
    // Update can send AdvanceDialogueServerRpc on mouse-click in addition to E key.
    private bool _awaitingScriptedInput;

    // Choice submission state — server-side only.
    private bool _choiceReceived;
    private int _pendingChoiceIndex;

    // Cached per ShowChoicesClientRpc so the local-player callback can read the text.
    private string[] _currentChoiceTexts;

    private void Awake() => Instance = this;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays a scripted dialogue with <paramref name="speaker"/> as the NPC.
    /// Must be called on the server. <paramref name="onComplete"/> fires on the server
    /// after the last node completes.
    /// </summary>
    public void PlayDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue, Action onComplete = null)
    {
        if (!IsServer) return;

        if (speaker == null || dialogue == null ||
            dialogue.nodes == null || dialogue.nodes.Length == 0)
        {
            Debug.LogError("[ScriptedDialogueRunner] PlayDialogue called with null or empty data.");
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(RunDialogue(speaker, dialogue, onComplete));
    }

    // -------------------------------------------------------------------------
    // Click-to-advance — supplements the E key handled by WaitForInputRoutine
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!_awaitingScriptedInput) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Ignore clicks that land on any UI element (e.g. choice buttons).
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            // Two-stage: first click completes the typewriter without skipping the line.
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        // Second click (or first when typewriter is already done): advance the sequence.
        DialogueManager.Instance.AdvanceDialogueServerRpc();
    }

    // -------------------------------------------------------------------------
    // Internal sequence
    // -------------------------------------------------------------------------

    private IEnumerator RunDialogue(SuspectCharacter speaker, ScriptedDialogue dialogue, Action onComplete)
    {
        ulong speakerNetId = speaker.GetComponent<NetworkObject>().NetworkObjectId;

        Debug.Log($"[ScriptedDialogueRunner] RunDialogue — IsSpawned={IsSpawned}, IsServer={IsServer}, speakerNetId={speakerNetId}");

        EnterScriptedModeClientRpc();
        yield return null; // flush RPCs before the first line

        foreach (var node in dialogue.nodes)
        {
            TriggerSpeakerAnimationClientRpc(speakerNetId, node.animationTrigger);
            yield return StartCoroutine(SayAndWait(speaker, node.npcLine));

            if (node.type == ScriptedDialogueNodeType.Choice)
                yield return StartCoroutine(PlayChoiceNode(speaker, node, speakerNetId));
        }

        ExitScriptedModeClientRpc();
        yield return null;

        onComplete?.Invoke();
        Debug.Log("[ScriptedDialogueRunner] Scripted dialogue complete.");
    }

    /// <summary>Says a line and waits for the player to press E / click before continuing.</summary>
    private IEnumerator SayAndWait(SuspectCharacter speaker, string text)
    {
        _awaitingScriptedInput = true;
        DialogueManager.Instance.SayDialogue(speaker, text, waitForInput: true);
        yield return StartCoroutine(DialogueManager.Instance.WaitForInputRoutine());
        _awaitingScriptedInput = false;
    }

    private IEnumerator PlayChoiceNode(SuspectCharacter speaker, ScriptedDialogueNode node, ulong speakerNetId)
    {
        if (node.choices == null || node.choices.Length < 2)
        {
            Debug.LogWarning("[ScriptedDialogueRunner] Choice node requires at least 2 choices. Skipping.");
            yield break;
        }

        _choiceReceived = false;
        _pendingChoiceIndex = -1;
        ShowChoicesClientRpc(node.choices[0].playerChoiceText, node.choices[1].playerChoiceText);

        yield return new WaitUntil(() => _choiceReceived);

        // Fire the response animation, then deliver the NPC's unique reply.
        var chosen = node.choices[_pendingChoiceIndex];
        TriggerSpeakerAnimationClientRpc(speakerNetId, chosen.animationTrigger);
        yield return StartCoroutine(SayAndWait(speaker, chosen.npcResponse));
    }

    // -------------------------------------------------------------------------
    // Client RPCs — mode
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void EnterScriptedModeClientRpc()
    {
        Debug.Log($"[ScriptedDialogueRunner] EnterScriptedModeClientRpc — " +
                  $"PlayerInstance={PlayerInstance.Instance != null}, " +
                  $"IsOutsideLocal={PlayerInstance.Instance?.IsOutsideLocal}, " +
                  $"ChoiceSystemInstance={DialogueChoiceSystem.Instance != null}");

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;
        DialogueChoiceSystem.Instance.EnterScriptedDialogueMode();
    }

    [ClientRpc]
    private void ExitScriptedModeClientRpc()
    {
        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;
        DialogueChoiceSystem.Instance.ExitScriptedDialogueMode();
    }

    // -------------------------------------------------------------------------
    // Client RPCs — choices
    // -------------------------------------------------------------------------

    [ClientRpc]
    private void ShowChoicesClientRpc(string choice0, string choice1)
    {
        _currentChoiceTexts = new[] { choice0, choice1 };

        if (PlayerInstance.Instance == null || PlayerInstance.Instance.IsOutsideLocal) return;

        DialogueChoiceSystem.Instance.ShowScriptedChoices(_currentChoiceTexts, OnLocalPlayerPickedChoice);
    }

    private void OnLocalPlayerPickedChoice(int choiceIndex)
    {
        string playerName = GetLocalPlayerName();
        string choiceText = _currentChoiceTexts != null ? _currentChoiceTexts[choiceIndex] : string.Empty;

        // SpawnSubtitles also logs to DialogueHistoryManager internally.
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.cyan, isPlayer: true);

        SubmitScriptedChoiceServerRpc(choiceIndex, playerName, choiceText);
    }

    // -------------------------------------------------------------------------
    // Server RPCs — choice submission
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sent by the local player after selecting a choice.
    /// The server broadcasts the player's line to all other clients and advances the sequence.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SubmitScriptedChoiceServerRpc(
        int choiceIndex, string playerName, string choiceText,
        ServerRpcParams rpcParams = default)
    {
        if (_choiceReceived) return; // only the first submission wins

        BroadcastPlayerChoiceClientRpc(choiceText, playerName, rpcParams.Receive.SenderClientId);
        _pendingChoiceIndex = choiceIndex;
        _choiceReceived = true;
    }

    [ClientRpc]
    private void BroadcastPlayerChoiceClientRpc(string choiceText, string playerName, ulong senderClientId)
    {
        // The sender already showed their own subtitle locally in OnLocalPlayerPickedChoice.
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.cyan, isPlayer: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fires an Animator trigger on the speaker on all clients.
    /// No-ops when <paramref name="triggerName"/> is null or empty.
    /// </summary>
    [ClientRpc]
    private void TriggerSpeakerAnimationClientRpc(ulong speakerNetId, string triggerName)
    {
        if (string.IsNullOrEmpty(triggerName)) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(speakerNetId, out var netObj))
        {
            var character = netObj.GetComponent<SuspectCharacter>();
            if (character?.animator != null)
                character.animator.SetTrigger(triggerName);
        }
    }

    private string GetLocalPlayerName()
    {
        var transport = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
            return Steamworks.SteamClient.Name;

        return $"Player {NetworkManager.Singleton.LocalClientId}";
    }
}
