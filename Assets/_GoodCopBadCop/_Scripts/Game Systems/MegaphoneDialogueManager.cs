using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles all voiced megaphone barks and on-screen dialogue text for the campaign.
/// Combines the bark system (animated speaker + audio + canvas text) and the simple
/// UI text overlay into a single manager.
///
/// Scripted sequences are exposed as public methods and can be called directly or
/// triggered by subscribing to CampaignManager.OnTutorialStepRequested.
///
/// Use <see cref="ShowDialogueSynced"/> from server-only tutorial coroutines to broadcast a
/// bark to all clients simultaneously. <see cref="ShowDialogue"/> remains for local-only barks
/// that the server triggers from gameplay events visible to all machines.
///
/// Set <see cref="disabled"/> to true in the Inspector or via debug tooling to suppress all output.
/// </summary>
public class MegaphoneDialogueManager : NetworkBehaviour
{
    public static MegaphoneDialogueManager Instance;

    [Header("Bark Canvas")]
    [SerializeField] private GameObject _barkCanvas;
    [SerializeField] private TextMeshProUGUI _barkText;
    [SerializeField] private Animator _speakerAnimator;

    [Header("Megaphone Camera")]
    [Tooltip("The camera that frames the megaphone speaker during tutorial cutscenes. " +
             "Assign the child 'Megaphone Camera' of this manager. " +
             "Use SetMegaphoneCameraActive to show/hide it from server-side tutorial coroutines.")]
    [SerializeField] private Transform _megaphoneCameraTransform;

    [Header("Dialogue Positioning")]
    [SerializeField] private RectTransform _megaphoneDialogueRect;
    [SerializeField] private RectTransform _dialogueTopPos;
    [SerializeField] private RectTransform _dialogueBottomPos;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClips;

    [Header("Settings")]
    [Tooltip("When true, all dialogue output is suppressed.")]
    public bool disabled = true;

    private static readonly int SpeakingParam = Animator.StringToHash("Speaking");
    private const float PostSpeakHideDuration = 3f;

    private bool _isSpeaking;
    private Coroutine _hideCoroutine;
    private Coroutine _positionTrackCoroutine;

    /// <summary>
    /// Authoritative speaking state replicated to all clients so server-side tutorial
    /// coroutines can WaitUntil(!IsSpeakingSynced) and know when the bark has finished
    /// on every machine.
    /// </summary>
    private readonly NetworkVariable<bool> _isSpeakingNetwork = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// True while a bark is playing. On the server this also drives the synced
    /// NetworkVariable so tutorial coroutines can await completion on all clients.
    /// Use this in server-only coroutines; it reflects the server's own speaking state.
    /// </summary>
    public bool IsSpeaking => _isSpeaking;

    /// <summary>
    /// Replicated speaking flag. Use in server-side WaitUntil checks so the coroutine
    /// waits for the bark to finish before advancing to the next tutorial beat.
    /// This is identical to IsSpeaking on the server but safe to poll from any context.
    /// </summary>
    public bool IsSpeakingSynced => _isSpeakingNetwork.Value;

    // Guards the one-time guidebook hint so it only fires the first time the guidebook is opened.
    private bool _guidebookHintShown;

    // ---------------------------------------------------------------------------
    // Unity / Network Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _barkCanvas.SetActive(false);

        ShiftManager.Instance.OnShiftStart += OnShiftStart;

        CampaignManager.OnTutorialStepRequested += HandleTutorialStep;
        GuidebookController.OnGuidebookOpened += OnGuidebookOpened;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
            ShiftManager.Instance.OnShiftStart -= OnShiftStart;

        CampaignManager.OnTutorialStepRequested -= HandleTutorialStep;
        GuidebookController.OnGuidebookOpened -= OnGuidebookOpened;
    }

    // ---------------------------------------------------------------------------
    // Gameplay Event Hooks
    // ---------------------------------------------------------------------------

    private void OnShiftStart()
    {
        // Day_01 owns all Day 1 welcome barks in its own sequenced coroutine — suppress auto-fire.
        if (ShiftManager.Instance != null && ShiftManager.Instance.CurrentDay == 1) return;
    }

    private IEnumerator Day1WelcomeBarkSequence()
    {
        if (disabled) yield break;

        yield return new WaitForSeconds(7f);
        ShowDialogue("Good morning, sunshine.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("Welcome to your first day on the job...");
        yield return new WaitForSeconds(5f);
        ShowDialogue("We've been waiting for you.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("The last guy didn't last very long. We're hoping you can do better.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("Judging by the looks of you, I give you a week, tops.");
        yield return new WaitForSeconds(5f);
        ShowDialogue("But to give you the best shot, I'll be here to help out.");
    }

    private void OnGuidebookOpened()
    {
        if (_guidebookHintShown) return;
        _guidebookHintShown = true;
        StartCoroutine(GuidebookOpenedHintCoroutine());
    }

    private IEnumerator GuidebookOpenedHintCoroutine()
    {
        yield return new WaitUntil(() => !_isSpeaking);
        yield return new WaitForSeconds(1f);
        ShowDialogue("Use Q and E to flip through the pages. It covers everything you need to know to do your job.");
    }

    // ---------------------------------------------------------------------------
    // Scripted Sequences (called by ShiftManager or CampaignManager)
    // ---------------------------------------------------------------------------

    /// <summary>Shown when the shift clock-out is ready.</summary>
    public void SayClockOutReady()
    {
        ShowDialogue("Your shift is over. Clock out to end the day.");
    }

    /// <summary>Shown when not all players are inside the booth at shift start.</summary>
    public void SayNotAllInside()
    {
        ShowDialogue("All inspectors must be inside the booth to begin the shift.");
    }

    /// <summary>Shown when a player tries to leave the booth during a locked shift.</summary>
    public void SayDoorLocked(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        ShowDialogue(lines[UnityEngine.Random.Range(0, lines.Length)]);
    }

    // ---------------------------------------------------------------------------
    // Tutorial Step Handler
    // ---------------------------------------------------------------------------

    private void HandleTutorialStep(TutorialStep step)
    {
        // Wire campaign tutorial steps to barks here as the tutorial system is built out.
        // Each case maps a TutorialStep enum value to a bark sequence or UI trigger.
        switch (step)
        {
            case TutorialStep.IntroDay1:
                // Handled via OnShiftStart → Day1WelcomeBarkSequence on the first day.
                break;
            case TutorialStep.NightTasksExplained:
                break;
        }
    }

    // ---------------------------------------------------------------------------
    // Networked Tutorial Marker Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Server-only: shows a tutorial arrow above <paramref name="target"/> on every client.
    /// Resolves the target by its <see cref="NetworkObject"/> so the reference is safe
    /// to send across the network.
    /// </summary>
    public void ShowMarkerSynced(NetworkObject target)
    {
        if (!IsServer || target == null) return;
        ShowMarkerClientRpc(target);
        // ClientRpc does not execute on the host; mark locally so the host also sees the arrow.
        TutorialMarkerManager.Instance?.Mark(target.transform);
    }

    /// <summary>
    /// Server-only: hides the tutorial arrow above <paramref name="target"/> on every client.
    /// </summary>
    public void HideMarkerSynced(NetworkObject target)
    {
        if (!IsServer || target == null) return;
        HideMarkerClientRpc(target);
        // Mirror the local unmark so the host stays in sync with clients.
        TutorialMarkerManager.Instance?.Unmark(target.transform);
    }

    [ClientRpc]
    private void ShowMarkerClientRpc(NetworkObjectReference targetRef)
    {
        if (!targetRef.TryGet(out NetworkObject netObj)) return;
        TutorialMarkerManager.Instance?.Mark(netObj.transform);
    }

    [ClientRpc]
    private void HideMarkerClientRpc(NetworkObjectReference targetRef)
    {
        if (!targetRef.TryGet(out NetworkObject netObj)) return;
        TutorialMarkerManager.Instance?.Unmark(netObj.transform);
    }

    /// <summary>Server-only: hides all active tutorial markers on every client.</summary>
    public void HideAllMarkersSynced()
    {
        if (!IsServer) return;
        HideAllMarkersClientRpc();
        // Mirror locally so the host also clears its markers.
        TutorialMarkerManager.Instance?.UnmarkAll();
    }

    [ClientRpc]
    private void HideAllMarkersClientRpc()
    {
        TutorialMarkerManager.Instance?.UnmarkAll();
    }

    /// <summary>
    /// Server-only: sets a scene GameObject active or inactive on every client, resolved by its
    /// full scene path. Use for plain (non-networked) scene GameObjects like tutorial arrows.
    /// </summary>
    public void SetGameObjectActiveSynced(Transform target, bool active)
    {
        if (!IsServer || target == null) return;
        string path = GetFullPath(target);
        SetGameObjectActiveClientRpc(path, active);
        // ClientRpc does not execute on the host; apply locally so the host also sees the change.
        target.gameObject.SetActive(active);
    }

    [ClientRpc]
    private void SetGameObjectActiveClientRpc(string transformPath, bool active)
    {
        Transform t = FindByPath(transformPath);
        if (t != null) t.gameObject.SetActive(active);
    }

    /// <summary>
    /// Server-only: shows a tutorial marker above a scene-static transform (e.g. a stamp slot or
    /// hand-off point) on every client. The transform path relative to scene root is sent so
    /// the client can resolve it without a NetworkObject reference.
    /// </summary>
    public void ShowStaticMarkerSynced(Transform target)
    {
        if (!IsServer || target == null) return;
        // Encode the path so any client can find the same Transform by walking the hierarchy.
        string path = GetFullPath(target);
        ShowStaticMarkerClientRpc(path);
        // ClientRpc does not execute on the host; mark locally so the host also sees the arrow.
        TutorialMarkerManager.Instance?.Mark(target);
    }

    /// <summary>Server-only: hides the tutorial marker above a scene-static transform on every client.</summary>
    public void HideStaticMarkerSynced(Transform target)
    {
        if (!IsServer || target == null) return;
        string path = GetFullPath(target);
        HideStaticMarkerClientRpc(path);
        // Mirror the local unmark so the host stays in sync with clients.
        TutorialMarkerManager.Instance?.Unmark(target);
    }

    [ClientRpc]
    private void ShowStaticMarkerClientRpc(string transformPath)
    {
        Transform t = FindByPath(transformPath);
        if (t != null) TutorialMarkerManager.Instance?.Mark(t);
    }

    [ClientRpc]
    private void HideStaticMarkerClientRpc(string transformPath)
    {
        Transform t = FindByPath(transformPath);
        if (t != null) TutorialMarkerManager.Instance?.Unmark(t);
    }

    /// <summary>Builds the full scene path of a Transform (e.g. "/Root/Child/Grandchild").</summary>
    private static string GetFullPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        Transform current = t.parent;
        while (current != null)
        {
            sb.Insert(0, current.name + "/");
            current = current.parent;
        }
        return "/" + sb.ToString();
    }

    /// <summary>Finds a Transform in the loaded scenes by its full scene path.</summary>
    private static Transform FindByPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        // Strip leading slash and split into parts.
        string[] parts = path.TrimStart('/').Split('/');
        if (parts.Length == 0) return null;

        // Find the root object.
        GameObject root = null;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            foreach (GameObject go in UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects())
            {
                if (go.name == parts[0]) { root = go; break; }
            }
            if (root != null) break;
        }
        if (root == null) return null;

        Transform current = root.transform;
        for (int i = 1; i < parts.Length; i++)
        {
            current = current.Find(parts[i]);
            if (current == null) return null;
        }
        return current;
    }

    // ---------------------------------------------------------------------------
    // Core Bark Display
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Server-only: displays a voiced bark on every client simultaneously.
    /// Use this from tutorial coroutines running on the server so the megaphone
    /// dialogue is synced between host and all clients. Ignored if already speaking.
    /// </summary>
    public void ShowDialogueSynced(string text)
    {
        if (!IsServer) return;
        if (disabled || _isSpeaking || _isSpeakingNetwork.Value) return;
        _isSpeakingNetwork.Value = true;
        ShowDialogueSyncedClientRpc(text);
    }

    /// <summary>
    /// Server-only: shows the PlayerTutorialUI text notification on every client simultaneously.
    /// Use from server-side tutorial coroutines (e.g. Day_01) when the UI needs to appear
    /// in sync on all machines without interrupting the bark system.
    /// </summary>
    public void ShowPlayerTutorialTextSynced(string text)
    {
        if (!IsServer) return;
        ShowPlayerTutorialTextClientRpc(text);
    }

    [ClientRpc]
    private void ShowPlayerTutorialTextClientRpc(string text)
    {
        PlayerTutorialUI.Instance?.ShowTextOnly(text);
    }

    /// <summary>
    /// Server-only: sets a temporary price override on the named <see cref="ShopItem"/> on every client.
    /// The item is located by its configured <see cref="ShopItem.Name"/>. Used by tutorial coroutines
    /// (e.g. Day_01) to make refill items free for all players simultaneously.
    /// </summary>
    public void SetShopItemPriceOverrideSynced(string itemName, int price)
    {
        if (!IsServer) return;
        SetShopItemPriceOverrideClientRpc(itemName, price);
    }

    [ClientRpc]
    private void SetShopItemPriceOverrideClientRpc(string itemName, int price)
    {
        // Resources.FindObjectsOfTypeAll includes loaded prefab assets, which is necessary
        // because ToolShopController.shopItems holds direct prefab asset references rather
        // than scene instances, so FindObjectsByType would never find the right ShopItem.
        foreach (ShopItem item in Resources.FindObjectsOfTypeAll<ShopItem>())
        {
            if (item.Name == itemName)
            {
                item.SetPriceOverride(price);
                // Refresh the matching shop view immediately in case the shop is already open.
                ToolShopController.Instance?.RefreshPriceForItem(item);
                break;
            }
        }
    }

    /// <summary>
    /// Server-only: clears the price override on the named <see cref="ShopItem"/> on every client,
    /// restoring its configured price. Paired with <see cref="SetShopItemPriceOverrideSynced"/>.
    /// </summary>
    public void ClearShopItemPriceOverrideSynced(string itemName)
    {
        if (!IsServer) return;
        ClearShopItemPriceOverrideClientRpc(itemName);
    }

    [ClientRpc]
    private void ClearShopItemPriceOverrideClientRpc(string itemName)
    {
        foreach (ShopItem item in Resources.FindObjectsOfTypeAll<ShopItem>())
        {
            if (item.Name == itemName)
            {
                item.ClearPriceOverride();
                // Refresh the matching shop view immediately — the locker stays open after
                // refill purchases (CloseShopOnPurchase = false), so OnEnable never re-fires
                // and without this the displayed price remains stale at 0.
                ToolShopController.Instance?.RefreshPriceForItem(item);
                break;
            }
        }
    }

    /// <summary>
    /// Server-only: activates or deactivates the megaphone camera on every client simultaneously.
    /// The camera is defined by the <c>_megaphoneCameraTransform</c> field assigned in the Inspector.
    /// Used by tutorial coroutines (e.g. Day_01) to cut to the speaker during bark sequences.
    /// </summary>
    public void SetMegaphoneCameraActive(bool active)
    {
        if (!IsServer) return;
        if (_megaphoneCameraTransform == null)
        {
            Debug.LogWarning("[MegaphoneDialogueManager] SetMegaphoneCameraActive: no camera assigned.");
            return;
        }
        SetGameObjectActiveSynced(_megaphoneCameraTransform, active);
    }

    /// <summary>
    /// The audio clips used for megaphone speech.
    /// Exposed so <see cref="ScriptedDialogueRunner"/> can route audio through the
    /// standard dialogue audio system when playing megaphone scripted dialogue.
    /// </summary>
    public AudioClip[] AudioClips => _audioClips;

    /// <summary>
    /// The AudioSource used for megaphone speech playback.
    /// Exposed so <see cref="ScriptedDialogueRunner"/> can play audio on the correct source.
    /// </summary>
    public AudioSource MegaphoneAudioSource => _audioSource;

    /// <summary>
    /// Server-only: marks the named <see cref="ShopItem"/> as available on every client and
    /// persists the unlock to the save file. The item will switch from '???' to its real
    /// name and price, and become purchasable.
    /// </summary>
    public void SetShopItemAvailableSynced(string itemName)
    {
        if (!IsServer) return;

        // Persist on the server so the unlock survives session restarts.
        SaveDataManager.Instance?.UnlockShopItem(itemName);

        SetShopItemAvailableClientRpc(itemName);
    }

    [ClientRpc]
    private void SetShopItemAvailableClientRpc(string itemName)
    {
        foreach (ShopItem item in Resources.FindObjectsOfTypeAll<ShopItem>())
        {
            if (item.Name == itemName)
            {
                item.SetAvailable(true);
                // Refresh the open shop UI immediately if it is visible.
                ToolShopController.Instance?.RefreshItemAvailability(item);
                break;
            }
        }
    }

    /// <summary>
    /// Broadcasts the bark to all clients including the host. Each client runs the
    /// visual/audio sequence independently in sync with the server's timing.
    /// </summary>
    [ClientRpc]
    private void ShowDialogueSyncedClientRpc(string text)
    {
        // The server already started its own bark via the NetworkVariable flag write above;
        // do not double-start. The ClientRpc still reaches the server/host machine, so guard.
        ShowDialogue(text);
    }

    /// <summary>
    /// Displays a voiced bark on the megaphone canvas. Ignored if already speaking.
    /// Audio is routed through DialogueManager for lip-sync and playback management.
    /// </summary>
    public void ShowDialogue(string text)
    {
        if (disabled || _isSpeaking) return;
        DialogueHistoryManager.Log(DialogueHistoryManager.SpeakerType.Megaphone, "Megaphone", text);
        StartCoroutine(ShowBarkSequence(text));
    }

    /// <summary>
    /// Displays plain text on the bark canvas without audio. Useful for lightweight prompts.
    /// Ignored if already speaking.
    /// </summary>
    public void ShowTextOnly(string text)
    {
        if (disabled || _isSpeaking) return;

        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        PositionDialogue();
        StartPositionTracking();

        _barkText.text = text;
        _barkCanvas.SetActive(true);

        // Dismiss any in-progress character dialogue subtitles so they don't overlap.
        DialogueManager.Instance?.ClearHistory();

        _hideCoroutine = StartCoroutine(WaitAndHide());
    }

    /// <summary>Immediately hides the bark canvas.</summary>
    public void HideDialogue()
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        StopPositionTracking();

        _barkCanvas.SetActive(false);
        _isSpeaking = false;

        if (IsServer)
            _isSpeakingNetwork.Value = false;

        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, false);
    }

    /// <summary>
    /// Sets the speaker animator's Speaking bool directly.
    /// Called by ScriptedDialogueRunner on all clients when a megaphone line starts or ends.
    /// </summary>
    public void SetSpeakerSpeaking(bool speaking)
    {
        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, speaking);
    }

    // ---------------------------------------------------------------------------
    // Internal Coroutines
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Snaps the megaphone dialogue panel to the top position when regular character
    /// dialogue subtitles are active, or to the bottom position otherwise.
    /// </summary>
    private void PositionDialogue()
    {
        if (_megaphoneDialogueRect == null) return;

        bool subtitlesActive = DialogueManager.Instance != null && DialogueManager.Instance.HasActiveSubtitles;
        RectTransform target = subtitlesActive ? _dialogueTopPos : _dialogueBottomPos;

        if (target != null)
        {
            _megaphoneDialogueRect.anchorMin = target.anchorMin;
            _megaphoneDialogueRect.anchorMax = target.anchorMax;
            _megaphoneDialogueRect.pivot = target.pivot;
            _megaphoneDialogueRect.anchoredPosition = target.anchoredPosition;
            _megaphoneDialogueRect.sizeDelta = target.sizeDelta;
        }
    }

    /// <summary>
    /// Starts a frame-by-frame coroutine that repositions the megaphone panel
    /// while the bark is visible.
    /// </summary>
    private void StartPositionTracking()
    {
        StopPositionTracking();
        _positionTrackCoroutine = StartCoroutine(TrackSubtitlePosition());
    }

    private void StopPositionTracking()
    {
        if (_positionTrackCoroutine != null)
        {
            StopCoroutine(_positionTrackCoroutine);
            _positionTrackCoroutine = null;
        }
    }

    private IEnumerator TrackSubtitlePosition()
    {
        while (true)
        {
            PositionDialogue();
            yield return null;
        }
    }

    private IEnumerator ShowBarkSequence(string text)
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _isSpeaking = true;
        _barkCanvas.SetActive(false);

        PositionDialogue();
        StartPositionTracking();

        yield return new WaitForSeconds(0.4f);

        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, true);

        _barkText.text = text;
        _barkCanvas.SetActive(true);

        // Dismiss any in-progress character dialogue subtitles so they don't overlap.
        DialogueManager.Instance?.ClearHistory();

        // Use the dedicated megaphone slot so that SayDialogueClientRpc (which calls
        // StopDialogueAudio on all clients) cannot cancel this bark mid-speech and
        // leave _isSpeaking stuck true.
        DialogueManager.Instance.PlayMegaphoneAudio(text, _audioClips, _audioSource, OnSpeakingFinished);
    }

    private void OnSpeakingFinished()
    {
        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, false);

        _isSpeaking = false;

        // Clear the authoritative flag so remote clients know the bark is done.
        if (IsServer)
            _isSpeakingNetwork.Value = false;

        _hideCoroutine = StartCoroutine(WaitAndHide());
    }

    private IEnumerator WaitAndHide()
    {
        yield return new WaitForSeconds(PostSpeakHideDuration);
        StopPositionTracking();
        _barkCanvas.SetActive(false);
    }
}
