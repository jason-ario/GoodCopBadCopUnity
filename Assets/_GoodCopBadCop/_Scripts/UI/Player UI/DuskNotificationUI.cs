using System.Collections;
using UnityEngine;

/// <summary>
/// Displays the "Dusk" notification banner the instant the last suspect for the day has been
/// processed (see <see cref="ShiftManager.OnDuskBegin"/>) — i.e. when the work shift's suspect
/// processing is done and the day's post-shift tasks begin, not when the player clocks out.
/// Fades in over <see cref="_fadeInDuration"/> seconds, holds for <see cref="_holdDuration"/> seconds,
/// then fades out over <see cref="_fadeOutDuration"/> seconds. Plays a bell chime on show.
///
/// The notification panel is auto-discovered as the first child with a CanvasGroup component.
/// </summary>
public class DuskNotificationUI : MonoBehaviour
{
    /// <summary>
    /// Singleton accessor so other systems (e.g. Day_01's trash/graffiti tutorial trigger) can
    /// query <see cref="TotalDisplayDuration"/> and wait for this banner to fully clear before
    /// showing their own top-of-screen overlay.
    /// </summary>
    public static DuskNotificationUI Instance { get; private set; }

    [Tooltip("Bell chime clip played when the notification appears.")]
    [SerializeField] private AudioClip _chimeClip;

    [Header("Timing")]
    [SerializeField] private float _fadeInDuration  = 1f;
    [SerializeField] private float _holdDuration    = 2f;
    [SerializeField] private float _fadeOutDuration = 1f;

    /// <summary>
    /// Total seconds the banner is on screen (fade in + hold + fade out). Callers that show
    /// their own top-of-screen overlay around the same moment as Dusk should wait at least this
    /// long first so the two don't visually overlap.
    /// </summary>
    public float TotalDisplayDuration => _fadeInDuration + _holdDuration + _fadeOutDuration;

    private GameObject _notificationPanel;
    private CanvasGroup _canvasGroup;
    private Animator    _panelAnimator;
    private Coroutine   _routine;

    private void Awake()
    {
        Instance = this;

        // Auto-discover the child panel — finds the first child CanvasGroup (inactive included).
        _canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        if (_canvasGroup != null)
        {
            _notificationPanel = _canvasGroup.gameObject;
            _panelAnimator     = _notificationPanel.GetComponent<Animator>();
        }
        else
        {
            Debug.LogError("[DuskNotificationUI] No CanvasGroup found in children. " +
                           "Ensure the notification panel child has a CanvasGroup component.", this);
        }
    }

    private void OnEnable()  => ShiftManager.OnDuskBegin += ShowDusk;
    private void OnDisable() => ShiftManager.OnDuskBegin -= ShowDusk;

    private void ShowDusk()
    {
        if (_notificationPanel == null || _canvasGroup == null) return;

        // Run the routine on UIController.Instance rather than on `this`. This GameObject lives
        // under UIController's "playerUI" root, which UIController.ClosePlayerUI/ShowPlayerUI
        // (dialogue, cutscenes, the pause menu, tool shop, HQ order screen, etc.) toggles active
        // on and off — and Unity kills a running Coroutine outright the instant its host is
        // disabled, with no way to resume it later. If that toggle landed mid fade-in/hold/
        // fade-out (very plausible right at Dusk, since a scripted dialogue exit often finishes
        // in the same moment Dusk fires), the routine's own cleanup at the end never got to run,
        // stranding the banner permanently visible — or, depending on exactly when the interrupt
        // landed, cutting the fade so short it looked like an instant pop in/out instead of a
        // smooth animation. UIController's own GameObject is never disabled, so hosting the
        // coroutine there guarantees the full fade-in/hold/fade-out always plays out, even while
        // this panel's ancestry is temporarily inactive (it simply won't render until the parent
        // reactivates, then resumes exactly where the animation left off).
        MonoBehaviour host = UIController.Instance != null ? (MonoBehaviour)UIController.Instance : this;

        if (_routine != null)
            host.StopCoroutine(_routine);

        _routine = host.StartCoroutine(NotificationRoutine());
    }

    private IEnumerator NotificationRoutine()
    {
        // Disable the Animator so we drive the CanvasGroup alpha directly.
        if (_panelAnimator != null)
            _panelAnimator.enabled = false;

        _notificationPanel.SetActive(true);
        _canvasGroup.alpha = 0f;

        if (_chimeClip != null && SFXController.Instance != null)
            SFXController.Instance.Play(_chimeClip);

        yield return FadeAlpha(0f, 1f, _fadeInDuration);
        yield return new WaitForSeconds(_holdDuration);
        yield return FadeAlpha(1f, 0f, _fadeOutDuration);

        _notificationPanel.SetActive(false);
        _routine = null;
    }

    private IEnumerator FadeAlpha(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            _canvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _canvasGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }

        _canvasGroup.alpha = to;
    }
}
