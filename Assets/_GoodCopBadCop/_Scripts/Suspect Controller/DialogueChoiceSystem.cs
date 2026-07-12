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

    // True after the local player has submitted their scripted choice — prevents re-picks
    // while waiting for the other player to choose.
    private bool _localChoiceLocked;

    // Cached player body mesh root — hidden while in dialogue mode, restored on exit.
    private GameObject _playerBody;

    // Cached player arms — hidden while in dialogue mode, restored on exit.
    private GameObject _playerArms;

    /// <summary>
    /// Matches the delay in <see cref="NPCRespondToDialogueChoice"/> so the choice panel
    /// re-appears the exact moment the NPC begins their response.
    /// </summary>
    private const float ResponseShowChoicesDelay = 1f;

    private Coroutine _reshowCoroutine;

    private void Awake()
    {
        Instance = this;
        // Static state survives between Editor Play Mode sessions — reset explicitly so a
        // session that ended mid-dialogue does not leave the guard permanently engaged.
        IsInDialogueMode = false;
    }

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
        PlayerInstance.Instance?.SetIsInCutscene(true);

        // Exit any open diegetic view (tool locker, mini fridge, etc.) before locking the player.
        DiegeticViewController.Current?.Close();

        // Break the player out of any ongoing held-item activity (e.g. mopping) so the
        // use animation and coroutine don't persist through the cutscene.
        ForceStopHeldObjectUse();

        var player = PlayerInstance.Instance;
        player.GetComponent<PlayerMovementController>().SetCanControl(false);
        player.GetComponent<PlayerMovementController>().SetCanLook(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);

        UIController.Instance.ShowCursor();

        HidePlayerBody();
        player.SetPlayerLightActive(false);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(true);
    }

    private void ExitDialogueMode()
    {
        IsInDialogueMode = false;
        PlayerInstance.Instance?.SetIsInCutscene(false);

        if (_reshowCoroutine != null)
        {
            StopCoroutine(_reshowCoroutine);
            _reshowCoroutine = null;
        }

        dialogueChoiceContainer.SetActive(false);
        backButton.SetActive(false);

        // If scripted dialogue is still active (e.g. old-style dialogue closed while a scripted
        // sequence was running), do not restore normal gameplay state — the scripted mode exit
        // path will do that when the sequence finishes. Keep the cursor visible so the player
        // can still advance lines and make choices.
        if (ScriptedDialogueRunner.IsScriptedModeActive)
        {
            UIController.Instance.ShowCursor();
            return;
        }

        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();

        var player = PlayerInstance.Instance;
        // Restore look before restoring control so the CanControl setter can re-lock the cursor.
        player.GetComponent<PlayerMovementController>().SetCanLook(true);
        player.GetComponent<PlayerMovementController>().SetCanControl(true);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(false);

        ShowPlayerBody();
        player.SetPlayerLightActive(true);

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

        if (IsInDialogueMode)
        {
            // Scripted mode is taking over while old-style dialogue mode was already active
            // (e.g. player clicked a suspect just before the scripted sequence fired).
            // Force-show the cursor and tear down the lingering choice UI so it cannot
            // later call ExitDialogueMode and hide the cursor mid-sequence.
            UIController.Instance.ShowCursor();
            if (_reshowCoroutine != null) { StopCoroutine(_reshowCoroutine); _reshowCoroutine = null; }
            dialogueChoiceContainer.SetActive(false);
            return;
        }

        IsInDialogueMode = true;
        PlayerInstance.Instance?.SetIsInCutscene(true);

        // Exit any open diegetic view (tool locker, mini fridge, etc.) before locking the player.
        DiegeticViewController.Current?.Close();

        // Break the player out of any ongoing held-item activity (e.g. mopping) so the
        // use animation and coroutine don't persist through the cutscene.
        ForceStopHeldObjectUse();

        var player = PlayerInstance.Instance;
        if (player == null) return;

        player.GetComponent<PlayerMovementController>()?.SetCanControl(false);
        player.GetComponent<PlayerMovementController>()?.SetCanLook(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);
        UIController.Instance.ShowCursor();

        // Rotate the player to face the suspect/booth before the camera cuts in.
        if (lookTarget != null)
            player.GetComponent<PlayerMovementController>()?.LookAtTarget(lookTarget);

        HidePlayerBody();
        player.SetPlayerLightActive(false);

        if (SuspectController.Instance != null)
            SuspectController.Instance.SetSuspectCamActive(true);

        Debug.Log("[DialogueChoiceSystem] EnterScriptedDialogueMode complete — movement locked, cam activated.");
    }

    /// <summary>
    /// Exits scripted dialogue mode, restoring player state. Delegates to the standard exit path.
    /// </summary>
    public void ExitScriptedDialogueMode() => ExitDialogueMode();

    /// <summary>
    /// Enters scripted dialogue mode for outside-world cutscenes. Locks player movement and
    /// shows the cursor but does NOT activate the booth suspect camera. Use for NPC dialogues
    /// that occur outside the interrogation booth (e.g. Vlad out-back sequence).
    /// </summary>
    public void EnterScriptedDialogueModeOutside(Transform lookTarget = null)
    {
        if (IsInDialogueMode)
        {
            // Same guard as EnterScriptedDialogueMode: scripted mode takes over, show cursor
            // and clear old choice UI so it cannot hide the cursor via ExitDialogueMode later.
            UIController.Instance.ShowCursor();
            if (_reshowCoroutine != null) { StopCoroutine(_reshowCoroutine); _reshowCoroutine = null; }
            dialogueChoiceContainer.SetActive(false);
            return;
        }

        IsInDialogueMode = true;
        PlayerInstance.Instance?.SetIsInCutscene(true);

        DiegeticViewController.Current?.Close();

        // Break the player out of any ongoing held-item activity (e.g. mopping) so the
        // use animation and coroutine don't persist through the cutscene.
        ForceStopHeldObjectUse();

        var player = PlayerInstance.Instance;
        if (player == null) return;

        player.GetComponent<PlayerMovementController>()?.SetCanControl(false);
        player.GetComponent<PlayerMovementController>()?.SetCanLook(false);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(true);
        UIController.Instance.ShowCursor();

        if (lookTarget != null)
            player.GetComponent<PlayerMovementController>()?.LookAtTarget(lookTarget);

        HidePlayerBody();
        player.SetPlayerLightActive(false);

        Debug.Log("[DialogueChoiceSystem] EnterScriptedDialogueModeOutside — movement locked, interaction disabled, light off.");
    }

    /// <summary>
    /// Exits the outside scripted dialogue mode, restoring player movement and hiding the cursor.
    /// The complement to <see cref="EnterScriptedDialogueModeOutside"/>. Does not touch the
    /// booth suspect camera because it was never activated.
    /// </summary>
    public void ExitScriptedDialogueModeOutside()
    {
        if (!IsInDialogueMode) return;

        IsInDialogueMode = false;
        PlayerInstance.Instance?.SetIsInCutscene(false);

        UIController.Instance.HideCursor();

        var player = PlayerInstance.Instance;
        if (player == null) return;

        // Restore look before restoring control so the CanControl setter can re-lock the cursor.
        player.GetComponent<PlayerMovementController>()?.SetCanLook(true);
        player.GetComponent<PlayerMovementController>()?.SetCanControl(true);
        player.GetComponent<PlayerInteractionController>()?.SetSuspectCamMode(false);

        ShowPlayerBody();
        player.SetPlayerLightActive(true);

        Debug.Log("[DialogueChoiceSystem] ExitScriptedDialogueModeOutside — movement restored, interaction enabled, light on.");
    }

    /// <summary>
    /// Shows the choice panel for a scripted dialogue node.
    /// <paramref name="onChosen"/> fires locally on the picking client when a button is clicked.
    /// </summary>
    public void ShowScriptedChoices(string[] choiceTexts, Action<int> onChosen)
    {
        _scriptedChoiceCallback = onChosen;
        _localChoiceLocked = false;
        ResetChoiceHighlights();
        InitializeChoices(choiceTexts);
        dialogueChoiceContainer.SetActive(true);
    }

    /// <summary>Hides the choice panel without exiting dialogue mode.</summary>
    public void HideChoicePanel()
    {
        dialogueChoiceContainer.SetActive(false);
        _scriptedChoiceCallback = null;
        _localChoiceLocked = false;
    }

    /// <summary>
    /// Highlights the choice at <paramref name="choiceIndex"/> with the "pending pick" visual.
    /// Safe to call on already-highlighted choices.
    /// </summary>
    public void HighlightChoice(int choiceIndex)
    {
        if (choiceIndex >= 0 && choiceIndex < dialogueChoices.Length)
            dialogueChoices[choiceIndex].SetPickedState(true);
    }

    /// <summary>Clears all pending-pick highlights on every choice button.</summary>
    public void ResetChoiceHighlights()
    {
        foreach (var choice in dialogueChoices)
            choice.SetPickedState(false);
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
        // Scripted path: highlight the pick and hand off to ScriptedDialogueRunner.
        // Do NOT hide the panel — both players must choose before the sequence continues.
        if (_scriptedChoiceCallback != null)
        {
            _localChoiceLocked = true;
            var callback = _scriptedChoiceCallback;
            _scriptedChoiceCallback = null; // prevent re-pick from this client
            OnLocalPlayerSpoke?.Invoke();
            callback.Invoke(choiceIndex);
            return;
        }

        // If scripted mode is still active but this player already picked, ignore the click.
        if (ScriptedDialogueRunner.IsScriptedModeActive) return;
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

    // ─── Body mesh helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Hides the local player's body mesh root ("Art" child) while in dialogue mode
    /// so it does not clip into the suspect camera view.
    /// </summary>
    private void HidePlayerBody()
    {
        if (PlayerInstance.Instance == null) return;
        _playerBody = PlayerInstance.Instance.transform.Find("Art")?.gameObject;
        if (_playerBody != null)
            _playerBody.SetActive(false);
        else
            Debug.LogWarning("[DialogueChoiceSystem] HidePlayerBody: could not find 'Art' child on player root — body will remain visible.");

        _playerArms = PlayerInstance.Instance.transform.Find("CinemachineCamera/Arms_Socket/Player_Arms")?.gameObject;
        if (_playerArms != null)
            _playerArms.SetActive(false);
        else
            Debug.LogWarning("[DialogueChoiceSystem] HidePlayerBody: could not find 'CinemachineCamera/Arms_Socket/Player_Arms' — arms will remain visible.");
    }

    /// <summary>
    /// Restores the local player's body mesh root after dialogue mode ends.
    /// Re-applies the held item's animator bool after the arms GameObject is re-enabled
    /// because Unity resets all Animator parameters to defaults on re-activation.
    /// </summary>
    private void ShowPlayerBody()
    {
        if (_playerArms != null)
        {
            _playerArms.SetActive(true);
            _playerArms = null;
            // Re-apply the held item's pickup animation after the Animator resets on re-enable,
            // so the player visually holds the item correctly when the cutscene ends.
            ReapplyHeldItemAnimatorState();
        }

        if (_playerBody != null)
        {
            _playerBody.SetActive(true);
            _playerBody = null;
        }
    }

    // ─── Activity interrupt helpers ─────────────────────────────────────────

    /// <summary>
    /// If the player is currently mid-use on a held item (e.g. mopping), stops the use action
    /// before entering dialogue mode so the activity animation and coroutine do not persist
    /// through the cutscene.
    /// </summary>
    private static void ForceStopHeldObjectUse()
    {
        var player = PlayerInstance.Instance;
        if (player == null) return;
        player.GetComponent<PlayerPickupController>()?.ForceStopUse();
    }

    /// <summary>
    /// Re-applies the currently held item's <see cref="PickableItemData.pickupAnimBool"/> to the
    /// local animators after the arms GameObject is re-enabled. Unity resets all Animator parameters
    /// to their default values when a GameObject is deactivated and reactivated, so without this
    /// call the held-item animation (e.g. exam hold pose) is lost after every dialogue cutscene.
    /// Uses <see cref="PlayerAnimationController.SetAnimBoolLocal"/> to avoid a redundant RPC —
    /// the original RPC from <see cref="PickableObject.OnEquipped"/> already handled other clients.
    /// </summary>
    private static void ReapplyHeldItemAnimatorState()
    {
        var player = PlayerInstance.Instance;
        if (player == null) return;
        var pickup = player.GetComponent<PlayerPickupController>();
        PickableItemData itemData = pickup?.HeldObject?.ItemData;
        if (itemData == null || string.IsNullOrEmpty(itemData.pickupAnimBool)) return;
        var pac = player.GetComponent<PlayerAnimationController>();
        pac?.SetAnimBoolLocal(itemData.pickupAnimBool, true);
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