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

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip[] _audioClips;

    [Header("Settings")]
    [Tooltip("When true, all dialogue output is suppressed.")]
    public bool disabled;

    private static readonly int SpeakingParam = Animator.StringToHash("Speaking");
    private const float PostSpeakHideDuration = 3f;

    private bool _isSpeaking;
    private Coroutine _hideCoroutine;

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

    // Set when the night-phase tasks are all done; consumed by OnShiftReady so the bark
    // only fires after a real between-shift task cycle, not on the initial day start.
    private bool _betweenShiftTasksCompleted;

    // Guards the one-time trash task hint so it only fires the first time the task is introduced.
    private bool _trashTaskHintShown;

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
        ShiftManager.Instance.OnShiftReady += OnShiftReady;
        GameManager.Instance.OnGameStart += OnGameStart;

        BetweenShiftTaskManager.OnAllTasksComplete += OnAllTasksComplete;
        CampaignManager.OnTutorialStepRequested += HandleTutorialStep;
        GuidebookController.OnGuidebookOpened += OnGuidebookOpened;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftStart -= OnShiftStart;
            ShiftManager.Instance.OnShiftReady -= OnShiftReady;
        }

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;

        BetweenShiftTaskManager.OnAllTasksComplete -= OnAllTasksComplete;
        CampaignManager.OnTutorialStepRequested -= HandleTutorialStep;
        GuidebookController.OnGuidebookOpened -= OnGuidebookOpened;
    }

    // ---------------------------------------------------------------------------
    // Gameplay Event Hooks
    // ---------------------------------------------------------------------------

    private void OnGameStart()
    {
        StartCoroutine(GameStartBarkCoroutine());
    }

    private IEnumerator GameStartBarkCoroutine()
    {
        yield return new WaitForSeconds(12f);

        if (PlayerInstance.Instance != null && PlayerInstance.Instance.IsOutside)
            ShowDialogue("All inspectors please report to duty.");
    }

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

    private void OnShiftReady()
    {
        // Only bark after a real night-phase task cycle, not on the initial day start
        // where OnShiftReady fires solely to prime the switch button.
        if (!_betweenShiftTasksCompleted) return;

        _betweenShiftTasksCompleted = false;
        ShowDialogue("All tasks completed, return to the booth for the next shift.");
    }

    private void OnAllTasksComplete()
    {
        _betweenShiftTasksCompleted = true;
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

    /// <summary>
    /// Plays when the end-of-shift report is dismissed.
    /// Delivers the end-of-shift barks and then triggers the night phase on ShiftManager
    /// after the task announcement, preserving the original deferred timing.
    /// </summary>
    public void SayEndOfShiftDialogue()
    {
        StartCoroutine(EndOfShiftDialogueSequence());
    }

    private IEnumerator EndOfShiftDialogueSequence()
    {
        ShowDialogue("Your shift is over.");
        yield return new WaitUntil(() => !_isSpeaking);
        yield return new WaitForSeconds(3f);

        ShowDialogue("Complete your tasks to prepare for your next shift.");
        yield return new WaitUntil(() => !_isSpeaking);
        yield return new WaitForSeconds(2f);

        AnnounceNewTask();
    }

    /// <summary>
    /// Shows the new task notification via PlayerTutorialUI, then triggers the night phase
    /// after a short delay so the guidebook badge activates in sync with the bark.
    /// </summary>
    private void AnnounceNewTask()
    {
        StartCoroutine(AnnounceNewTaskCoroutine());
    }

    private IEnumerator AnnounceNewTaskCoroutine()
    {
        if (PlayerTutorialUI.Instance != null)
            PlayerTutorialUI.Instance.ShowTextOnly("New task: Take out the trash.");

        yield return new WaitForSeconds(4f);

        ShiftManager.Instance.TriggerBeginNightPhase();

        if (!_trashTaskHintShown)
        {
            _trashTaskHintShown = true;
            yield return new WaitForSeconds(3f);
            ShowDialogue("Bring the trash bags to the dumpster. They are scattered throughout the yard.");
            yield return new WaitUntil(() => !_isSpeaking);
            yield return new WaitForSeconds(3f);
            ShowDialogue("Press Tab to open your guidebook. Your tasks and everything you need to do your job are inside.");
        }
    }

    /// <summary>Played when between-shift tasks are all done and the booth is ready.</summary>
    public void SayBetweenShiftReady()
    {
        ShowDialogue("You may now prepare for your next shift.");
    }

    /// <summary>Played as an immediate prompt to start the next shift.</summary>
    public void SayBeginShiftNow()
    {
        ShowDialogue("Begin your next shift immediately.");
    }

    /// <summary>Shown when a player tries to leave the booth during a locked shift.</summary>
    public void SayDoorLocked(string[] lines)
    {
        if (lines == null || lines.Length == 0) return;
        ShowDialogue(lines[UnityEngine.Random.Range(0, lines.Length)]);
    }

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
                SayBetweenShiftReady();
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
    /// Marks the one-time trash task hint as already shown so it is not repeated
    /// when the end-of-shift dialogue runs (e.g. because Day_01 showed it during the shift).
    /// </summary>
    public void MarkTrashTaskHintShown()
    {
        _trashTaskHintShown = true;
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

        _barkCanvas.SetActive(false);
        _isSpeaking = false;

        if (IsServer)
            _isSpeakingNetwork.Value = false;

        if (_speakerAnimator != null)
            _speakerAnimator.SetBool(SpeakingParam, false);
    }

    // ---------------------------------------------------------------------------
    // Internal Coroutines
    // ---------------------------------------------------------------------------

    private IEnumerator ShowBarkSequence(string text)
    {
        if (_hideCoroutine != null)
            StopCoroutine(_hideCoroutine);

        _isSpeaking = true;
        _barkCanvas.SetActive(false);

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
        _barkCanvas.SetActive(false);
    }
}
