using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DialogueChoiceSystem : NetworkBehaviour
{
    /// <summary>
    /// Fired on the local client immediately when the local player selects a dialogue choice.
    /// Subscribe in <see cref="PlayerTalkingAnimationController"/> to trigger talking animations.
    /// </summary>
    public static event Action OnLocalPlayerSpoke;

    /// <summary>
    /// True while the local player is locked into an active dialogue session with a suspect.
    /// Used by <see cref="SuspectController"/> to prevent the arrival cam from closing while
    /// dialogue mode is holding it open.
    /// </summary>
    public static bool IsInDialogueMode { get; private set; }

    [SerializeField] private DialogueChoice[] dialogueChoices;
    [SerializeField] private GameObject dialogueChoiceContainer;
    [SerializeField] private Subtitles subtitlesPrefab;
    [SerializeField] private RectTransform subtitlesContainer;

    /// <summary>
    /// Matches the delay in <see cref="NPCRespondToDialogueChoice"/> so the choice panel
    /// re-appears the exact moment the NPC begins their response.
    /// </summary>
    private const float ResponseShowChoicesDelay = 1f;

    private Coroutine _reshowCoroutine;

    /// <summary>
    /// Opens the dialogue choice UI. On the first call, enters dialogue mode: locks movement,
    /// activates the suspect cam, and shows the back button. Safe to call if already in mode.
    /// </summary>
    public void StartDialogueChoices(Transform lookTarget, string[] choices)
    {
        if (!IsInDialogueMode)
            EnterDialogueMode();

        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(lookTarget);
        InitializeChoices(choices);
        dialogueChoiceContainer.SetActive(true);
    }

    private void EnterDialogueMode()
    {
        IsInDialogueMode = true;

        var player = PlayerInstance.Instance;
        player.GetComponent<PlayerMovementController>().SetCanControl(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);

        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(ExitDialogueMode);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(true);
    }

    private void ExitDialogueMode()
    {
        IsInDialogueMode = false;

        if (_reshowCoroutine != null)
        {
            StopCoroutine(_reshowCoroutine);
            _reshowCoroutine = null;
        }

        dialogueChoiceContainer.SetActive(false);
        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();

        var player = PlayerInstance.Instance;
        player.GetComponent<PlayerMovementController>().SetCanControl(true);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(false);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(false);
    }

    private void InitializeChoices(string[] choices)
    {
        for (var i = 0; i < choices.Length; i++)
        {
            dialogueChoices[i].SetChoiceText(choices[i]);
        }
    }

    public void ChooseDialogueChoice(int choiceIndex)
    {
        // Hide the panel but stay in dialogue mode — it will re-appear once the NPC starts responding.
        dialogueChoiceContainer.SetActive(false);
        OnLocalPlayerSpoke?.Invoke();

        string playerName = GetPlayerName();

        // Log locally for the sending player — other clients log via SpawnPlayerSubtitleClientRpc.
        DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Player, playerName, dialogueChoices[choiceIndex].choiceText);

        ChooseDialogueChoiceServerRpc(choiceIndex, playerName);

        if (_reshowCoroutine != null)
            StopCoroutine(_reshowCoroutine);
        _reshowCoroutine = StartCoroutine(ReshowChoicesAfterDelay());
    }

    /// <summary>
    /// Re-displays the choice panel the moment the NPC begins their response,
    /// so the player can queue their next question or press Back to exit.
    /// </summary>
    private IEnumerator ReshowChoicesAfterDelay()
    {
        yield return new WaitForSeconds(ResponseShowChoicesDelay);
        _reshowCoroutine = null;

        if (IsInDialogueMode)
            dialogueChoiceContainer.SetActive(true);
    }

    private string GetPlayerName()
    {
        var transport = Unity.Netcode.NetworkManager.Singleton.NetworkConfig.NetworkTransport;

        if (transport is Netcode.Transports.Facepunch.FacepunchTransport)
            return Steamworks.SteamClient.Name;

        return $"Player {Unity.Netcode.NetworkManager.Singleton.LocalClientId}";
    }

    [ServerRpc(RequireOwnership = false)]
    private void ChooseDialogueChoiceServerRpc(int choiceIndex, string playerName, ServerRpcParams serverRpcParams = default)
    {
        ulong senderClientId = serverRpcParams.Receive.SenderClientId;
        SpawnPlayerSubtitleClientRpc(dialogueChoices[choiceIndex].choiceText, playerName, senderClientId);
        StartCoroutine(NPCRespondToDialogueChoice(choiceIndex));
    }

    [ClientRpc]
    private void SpawnPlayerSubtitleClientRpc(string choiceText, string playerName, ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        DialogueManager.Instance.SpawnSubtitles(choiceText, playerName, Color.darkCyan, true);
    }

    private IEnumerator NPCRespondToDialogueChoice(int choiceIndex)
    {
        yield return new WaitForSeconds(1);
        SuspectController.Instance.RespondToDialogueChoice(choiceIndex);
    }

    /// <summary>Exits dialogue mode fully. Called by the Back button and by any external system that needs to end dialogue.</summary>
    public void CloseDialogueChoices()
    {
        ExitDialogueMode();
    }
}