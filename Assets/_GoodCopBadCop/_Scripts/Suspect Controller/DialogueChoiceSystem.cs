using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class DialogueChoiceSystem : NetworkBehaviour
{
    /// <summary>Singleton — set automatically in Awake.</summary>
    public static DialogueChoiceSystem Instance { get; private set; }

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
    [SerializeField] private GameObject backButton;

    // Callback set by ShowScriptedChoices — routes the player's pick to ScriptedDialogueRunner.
    private Action<int> _scriptedChoiceCallback;

    /// <summary>
    /// Matches the delay in <see cref="NPCRespondToDialogueChoice"/> so the choice panel
    /// re-appears the exact moment the NPC begins their response.
    /// </summary>
    private const float ResponseShowChoicesDelay = 1f;

    private Coroutine _reshowCoroutine;

    private void Awake() => Instance = this;

    /// <summary>
    /// Opens the dialogue choice UI. On the first call, enters dialogue mode: locks movement
    /// and activates the suspect cam. Safe to call if already in mode.
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

        // Exit any open diegetic view (tool locker, mini fridge, etc.) before locking the player.
        DiegeticViewController.Current?.Close();

        var player = PlayerInstance.Instance;
        player.GetComponent<PlayerMovementController>().SetCanControl(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);

        UIController.Instance.ShowCursor();

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
        backButton.SetActive(false);
        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();

        var player = PlayerInstance.Instance;
        player.GetComponent<PlayerMovementController>().SetCanControl(true);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(false);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(false);
    }

    // -------------------------------------------------------------------------
    // Scripted dialogue API — used by ScriptedDialogueRunner
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enters dialogue mode for a scripted cutscene: locks player movement, activates the
    /// suspect camera, and shows the cursor. Exits any active diegetic view and rotates the
    /// player toward <paramref name="lookTarget"/> if provided. Does not display the back button.
    /// </summary>
    public void EnterScriptedDialogueMode(Transform lookTarget = null)
    {
        Debug.Log($"[DialogueChoiceSystem] EnterScriptedDialogueMode — IsInDialogueMode={IsInDialogueMode}, " +
                  $"PlayerInstance={PlayerInstance.Instance != null}, " +
                  $"SuspectController={SuspectController.Instance != null}");

        if (IsInDialogueMode) return;

        IsInDialogueMode = true;

        // Exit any open diegetic view (tool locker, mini fridge, etc.) before locking the player.
        DiegeticViewController.Current?.Close();

        var player = PlayerInstance.Instance;
        if (player == null) return;

        player.GetComponent<PlayerMovementController>()?.SetCanControl(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);
        UIController.Instance.ShowCursor();

        // Rotate the player to face the suspect/booth before the camera cuts in.
        if (lookTarget != null)
            player.GetComponent<PlayerMovementController>()?.LookAtTarget(lookTarget);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(true);

        Debug.Log("[DialogueChoiceSystem] EnterScriptedDialogueMode complete — movement locked, cam activated.");
    }

    /// <summary>
    /// Exits scripted dialogue mode, restoring player state. Delegates to the standard exit path.
    /// </summary>
    public void ExitScriptedDialogueMode() => ExitDialogueMode();

    /// <summary>
    /// Shows the choice panel for a scripted dialogue node.
    /// <paramref name="onChosen"/> fires locally on the picking client when a button is clicked.
    /// </summary>
    public void ShowScriptedChoices(string[] choiceTexts, Action<int> onChosen)
    {
        _scriptedChoiceCallback = onChosen;
        InitializeChoices(choiceTexts);
        dialogueChoiceContainer.SetActive(true);
    }

    /// <summary>Hides the choice panel without exiting dialogue mode.</summary>
    public void HideChoicePanel()
    {
        dialogueChoiceContainer.SetActive(false);
        _scriptedChoiceCallback = null;
    }

    private void InitializeChoices(string[] choices)
    {
        for (var i = 0; i < dialogueChoices.Length; i++)
        {
            bool active = i < choices.Length;
            dialogueChoices[i].gameObject.SetActive(active);
            if (active)
                dialogueChoices[i].SetChoiceText(choices[i]);
        }
    }

    public void ChooseDialogueChoice(int choiceIndex)
    {
        // Scripted path: hide choices and hand off to ScriptedDialogueRunner's callback.
        if (_scriptedChoiceCallback != null)
        {
            dialogueChoiceContainer.SetActive(false);
            var callback = _scriptedChoiceCallback;
            _scriptedChoiceCallback = null;
            OnLocalPlayerSpoke?.Invoke();
            callback.Invoke(choiceIndex);
            return;
        }

        // Original interactive-dialogue path.
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
    /// Waits for the NPC's subtitle response to appear and then fully disappear before
    /// re-displaying the choice panel, so the player is never looking at both simultaneously.
    /// </summary>
    private IEnumerator ReshowChoicesAfterDelay()
    {
        // Minimum wait that matches the NPC's response delay, ensuring we don't
        // poll HasActiveSubtitles before the subtitle has had a chance to spawn.
        yield return new WaitForSeconds(ResponseShowChoicesDelay);

        // Wait for the NPC subtitle to arrive (accounts for network latency variance).
        yield return new WaitUntil(() => DialogueManager.Instance.HasActiveSubtitles);

        // Wait for the NPC subtitle to fully clear (typewriter + linger + destroy).
        yield return new WaitUntil(() => !DialogueManager.Instance.HasActiveSubtitles);

        _reshowCoroutine = null;

        if (IsInDialogueMode)
            dialogueChoiceContainer.SetActive(true);
    }

    private void Update()
    {
        if (!IsInDialogueMode) return;
        if (_reshowCoroutine == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        // Ignore clicks that land on a UI element (e.g. the Back button).
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

        SkipNPCLine();
    }

    /// <summary>
    /// Skips the current NPC line with two-stage behaviour:
    /// - If the subtitle is still typing, completes the reveal instantly without skipping.
    /// - If the text is fully revealed, stops audio, clears subtitles, and shows choices immediately.
    /// </summary>
    private void SkipNPCLine()
    {
        if (DialogueManager.Instance.IsAnySubtitleRevealing())
        {
            DialogueManager.Instance.CompleteCurrentReveal();
            return;
        }

        if (_reshowCoroutine != null)
        {
            StopCoroutine(_reshowCoroutine);
            _reshowCoroutine = null;
        }

        DialogueManager.Instance.SkipCurrentLine();

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