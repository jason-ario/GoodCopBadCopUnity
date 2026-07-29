using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Singleton controller for the top-of-screen tutorial overlay.
/// Slides in from the top, displays a named tutorial screen, and dismisses
/// when the player holds R until the fill bar is full.
///
/// Usage: TutorialOverlay.Instance.ShowMovementTutorial();
/// </summary>
public class TutorialOverlay : MonoBehaviour
{
    public static TutorialOverlay Instance { get; private set; }

    private static readonly int IsShowingHash = Animator.StringToHash("IsShowing");

    [Header("References")]
    [SerializeField] private Animator topTutorialAnimator;
    [SerializeField] private Transform screensContainer;
    [SerializeField] private Image holdToCloseFill;

    [Header("Tutorial Screens")]
    [SerializeField] private GameObject movementTutorialScreen;
    [SerializeField] private GameObject handlingItemsTutorialScreen;
    [SerializeField] private GameObject accuracyPayoutTutorialScreen;
    [SerializeField] private GameObject trashTutorialScreen;
    [SerializeField] private GameObject graffitiTutorialScreen;
    [SerializeField] private GameObject sortingMailTutorialScreen;
    [SerializeField] private GameObject killCriteriaTutorialScreen;
    [SerializeField] private GameObject fixPowerTutorialScreen;

    [Header("Settings")]
    [Tooltip("Seconds the player must hold R to close the overlay.")]
    [SerializeField] private float holdDuration = 2f;
    [Tooltip("Multiplier for how fast the fill drains when R is released.")]
    [SerializeField] private float fillDrainMultiplier = 2f;
    [Tooltip("Seconds to wait after triggering the close animation before deactivating the root.")]
    [SerializeField] private float hideDelay = 1.1f;

    private bool _isShowing;
    private float _holdProgress;
    private Coroutine _hideCoroutine;
    private Action _onCloseCallback;

    // ── Unity Lifecycle ─────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        topTutorialAnimator.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!_isShowing) return;

        if (Input.GetKey(KeyCode.R) || (Gamepad.current?.buttonWest.isPressed ?? false))
        {
            _holdProgress = Mathf.Min(_holdProgress + Time.deltaTime, holdDuration);
        }
        else
        {
            _holdProgress = Mathf.Max(0f, _holdProgress - Time.deltaTime * fillDrainMultiplier);
        }

        holdToCloseFill.fillAmount = _holdProgress / holdDuration;

        if (_holdProgress >= holdDuration)
            Close();
    }

    // ── Public API ──────────────────────────────────────────────────────

    /// <summary>Shows the movement tutorial overlay, sliding down from the top of the screen.</summary>
    public void ShowMovementTutorial(Action onComplete = null) => ShowScreen(movementTutorialScreen, onComplete);

    /// <summary>Shows the handling-items tutorial overlay (how to pick up and hold objects).</summary>
    public void ShowHandlingItemsTutorial(Action onComplete = null) => ShowScreen(handlingItemsTutorialScreen, onComplete);

    /// <summary>Shows the accuracy-payout tutorial overlay (more accurate anomaly marking = more coupons).</summary>
    public void ShowAccuracyPayoutTutorial(Action onComplete = null) => ShowScreen(accuracyPayoutTutorialScreen, onComplete);

    /// <summary>Shows the end-of-shift trash task tutorial overlay.</summary>
    public void ShowTrashTutorial(Action onComplete = null) => ShowScreen(trashTutorialScreen, onComplete);

    /// <summary>Shows the end-of-shift graffiti task tutorial overlay.</summary>
    public void ShowGraffitiTutorial(Action onComplete = null) => ShowScreen(graffitiTutorialScreen, onComplete);

    /// <summary>Shows the sorting-mail tutorial overlay (Day 2, right after Vlad unlocks the tool locker).</summary>
    public void ShowSortingMailTutorial(Action onComplete = null) => ShowScreen(sortingMailTutorialScreen, onComplete);

    /// <summary>
    /// Shows the kill-criteria tutorial overlay (Day 2 kill tutorial) — explains the
    /// quarantine/kill symptom-count thresholds right after the kill scripted dialogue.
    /// </summary>
    public void ShowKillCriteriaTutorial(Action onComplete = null) => ShowScreen(killCriteriaTutorialScreen, onComplete);

    /// <summary>
    /// Shows the "go fix the power outage at the electrical panel" tutorial overlay.
    /// Used when a booth power outage forces the player to solve the panel puzzle before the
    /// switch button will let them summon the next suspect (e.g. Day 2's Ocho encounter).
    /// </summary>
    public void ShowElectricalPanelTutorial(Action onComplete = null) => ShowScreen(fixPowerTutorialScreen, onComplete);

    /// <summary>
    /// Triggers the slide-out animation and deactivates the overlay after the animation completes.
    /// Safe to call even if the overlay is not currently showing.
    /// </summary>
    public void Close()
    {
        if (!_isShowing) return;

        _isShowing = false;
        topTutorialAnimator.SetBool(IsShowingHash, false);

        if (_hideCoroutine != null) StopCoroutine(_hideCoroutine);
        _hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    // ── Private ─────────────────────────────────────────────────────────

    private void ShowScreen(GameObject screen, Action onComplete = null)
    {
        if (screen == null)
        {
            Debug.LogError("[TutorialOverlay] Tutorial screen reference is null. Assign it in the Inspector.", this);
            return;
        }

        SetAllScreensInactive();
        screen.SetActive(true);

        // Cancel any in-progress close animation before re-showing.
        if (_hideCoroutine != null)
        {
            StopCoroutine(_hideCoroutine);
            _hideCoroutine = null;
        }

        _onCloseCallback = onComplete;
        gameObject.SetActive(true);
        _holdProgress = 0f;
        holdToCloseFill.fillAmount = 0f;
        _isShowing = true;
        topTutorialAnimator.gameObject.SetActive(true);
        topTutorialAnimator.SetBool(IsShowingHash, true);
    }

    private void SetAllScreensInactive()
    {
        if (screensContainer == null) return;
        foreach (Transform child in screensContainer)
            child.gameObject.SetActive(false);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelay);
        topTutorialAnimator.gameObject.SetActive(false);
        _hideCoroutine = null;

        Action callback = _onCloseCallback;
        _onCloseCallback = null;
        callback?.Invoke();
    }
}
