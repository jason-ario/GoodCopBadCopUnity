using System;
using System.Collections;
using UnityEngine;

public class PlayerTutorialUI : MonoBehaviour
{
    public static PlayerTutorialUI Instance { get; private set; }

    private static readonly int BlackBarsOn = Animator.StringToHash("BlackBarsOn");

    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private float defaultHoldDuration = 3f;

    private Animator _animator;
    private Coroutine _sequenceCoroutine;
    private Action _onCloseCallback;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _animator = GetComponent<Animator>();

        if (textReveal == null)
            Debug.LogError("[PlayerTutorialUI] textReveal is not assigned. Assign the Text (TMP) child in the Inspector.", this);

        // Fire when the local player first spawns — works across all start paths
        // (normal lobby, debug skip, autoStart) without needing OnGameStart.
        PlayerInstance.OnLocalPlayerSpawned += OnLocalPlayerSpawned;
    }

    private void Start()
    {
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        PlayerInstance.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;
    }

    /// <summary>
    /// Fires once when the local player's NetworkObject spawns. Shows the movement tutorial
    /// on Day 1 only; one-shot so respawns never re-trigger it.
    /// </summary>
    private void OnLocalPlayerSpawned()
    {
        PlayerInstance.OnLocalPlayerSpawned -= OnLocalPlayerSpawned;

        if (DebugConsole.Instance != null && DebugConsole.Instance.skipToBoothReady) return;
        if (CampaignManager.Instance == null || CampaignManager.Instance.CurrentDay != 1) return;

        gameObject.SetActive(true);
        StartCoroutine(DelayedShowBoothMessage(3f));
    }

    private IEnumerator DelayedShowBoothMessage(float delay)
    {
        yield return new WaitForSeconds(delay);

        ShowBoothMessage();
    }

    private void ShowBoothMessage()
    {
        Show("Go to the booth to start your shift.", -1f, ShowMovementTutorial);
    }

    private void ShowMovementTutorial()
    {
        if (TutorialOverlay.Instance != null)
            TutorialOverlay.Instance.ShowMovementTutorial();
    }

    /// <summary>Shows the cinematic black bars without any text. Holds for the specified duration then dismisses. Interrupts any running sequence.</summary>
    public void ShowBarsOnly(float holdDuration = -1f)
    {
        gameObject.SetActive(true);
        float duration = holdDuration < 0f ? defaultHoldDuration : holdDuration;
        StopSequence();
        _onCloseCallback = null;
        _sequenceCoroutine = StartCoroutine(BarsOnlySequenceCoroutine(duration));
    }

    private IEnumerator BarsOnlySequenceCoroutine(float holdDuration)
    {
        SetPlayerUIActive(false);
        _animator.SetBool(BlackBarsOn, true);

        yield return new WaitForSeconds(holdDuration);

        Dismiss();
    }

    /// <summary>Shows the tutorial bars with the given message, holds for the specified duration, then hides. Interrupts any running sequence.</summary>
    public void Show(string message, float holdDuration = -1f, Action onComplete = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning("[PlayerTutorialUI] Show called with a null or empty message. Ignoring.", this);
            return;
        }

        gameObject.SetActive(true);
        float duration = holdDuration < 0f ? defaultHoldDuration : holdDuration;

        StopSequence();
        _onCloseCallback = onComplete;
        _sequenceCoroutine = StartCoroutine(SequenceCoroutine(message, duration));
    }

    /// <summary>
    /// Shows the message as a text reveal without the black bar animation or player UI toggling.
    /// Use this for lightweight notifications that should not interrupt gameplay visuals.
    /// </summary>
    public void ShowTextOnly(string message, float holdDuration = -1f)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            Debug.LogWarning("[PlayerTutorialUI] ShowTextOnly called with a null or empty message. Ignoring.", this);
            return;
        }

        gameObject.SetActive(true);
        float duration = holdDuration < 0f ? defaultHoldDuration : holdDuration;

        StopSequence();
        _onCloseCallback = null;
        _sequenceCoroutine = StartCoroutine(TextOnlySequenceCoroutine(message, duration));
    }

    /// <summary>Immediately dismisses the current tutorial sequence and slides the bars out.</summary>
    public void Dismiss()
    {
        StopSequence();
        textReveal.Clear();
        textReveal.gameObject.SetActive(false);
        _animator.SetBool(BlackBarsOn, false);
        SetPlayerUIActive(true);
        gameObject.SetActive(false);

        Action callback = _onCloseCallback;
        _onCloseCallback = null;
        callback?.Invoke();
    }

    private void SetPlayerUIActive(bool active)
    {
        if (PlayerUI.Instance != null)
            PlayerUI.Instance.gameObject.SetActive(active);
        else
            Debug.LogWarning("[PlayerTutorialUI] PlayerUI.Instance is null — could not toggle Player UI.", this);
    }

    private IEnumerator SequenceCoroutine(string message, float holdDuration)
    {
        SetPlayerUIActive(false);

        textReveal.gameObject.SetActive(true);
        _animator.SetBool(BlackBarsOn, true);

        yield return textReveal.RevealText(message);
        yield return new WaitForSeconds(holdDuration);

        Dismiss();
    }

    private IEnumerator TextOnlySequenceCoroutine(string message, float holdDuration)
    {
        textReveal.gameObject.SetActive(true);

        yield return textReveal.RevealText(message);
        yield return new WaitForSeconds(holdDuration);

        textReveal.Clear();
        textReveal.gameObject.SetActive(false);
        _sequenceCoroutine = null;
        gameObject.SetActive(false);
    }

    private void StopSequence()
    {
        if (_sequenceCoroutine != null)
        {
            StopCoroutine(_sequenceCoroutine);
            _sequenceCoroutine = null;
        }
    }
}
