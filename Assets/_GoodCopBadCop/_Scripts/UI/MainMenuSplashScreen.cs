using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Splash sequence shown on the first load of the session, played over the live main menu scene:
/// <list type="number">
/// <item>The black backdrop fades away immediately, revealing the running menu scene (cameras,
/// cutscene and music are started by the caller at the same time).</item>
/// <item>Each logo in <see cref="logos"/> fades in, holds, and fades out over the live scene.</item>
/// <item>The main menu UI (the <c>menuGroup</c> passed to <see cref="TryPlay"/>) fades in.</item>
/// </list>
///
/// Holding any key, mouse button, or gamepad face/Start button for <see cref="holdToSkipDuration"/>
/// skips the current logo only — it fades out quickly and the next logo begins. The input must be
/// released before another skip can be charged. An optional prompt/progress bar shows hold progress.
///
/// The <see cref="EventSystem"/> stays enabled the whole time (disabling it, or its input module,
/// either replays stale clicks on re-enable or drops the pointer so hover stops working until the
/// mouse moves). Instead the full-screen backdrop absorbs pointer input while the splash plays, and
/// the menu <c>menuGroup</c> is non-interactable with nothing selected, so navigation/submit do nothing.
/// Input is handed to the menu the moment it starts fading in. Use <see cref="IsPlaying"/> to guard
/// any non-EventSystem input.
///
/// The splash only plays once per application session — reloading <c>Main.unity</c> skips it.
/// </summary>
public class MainMenuSplashScreen : MonoBehaviour
{
    [Header("References")]
    [Tooltip("CanvasGroup on the splash root. Kept fully visible while the splash plays.")]
    [SerializeField] private CanvasGroup rootGroup;

    [Tooltip("CanvasGroup on the full-screen black backdrop. Fades out at the start to reveal the live menu scene.")]
    [SerializeField] private CanvasGroup backgroundGroup;

    [Tooltip("Logos shown in order. Each must have its own CanvasGroup.")]
    [SerializeField] private CanvasGroup[] logos;

    [Tooltip("Scene EventSystem. Kept enabled; its selection is cleared while the splash plays so navigation/submit can't reach the menu.")]
    [SerializeField] private EventSystem eventSystem;

    [Header("Timing (seconds, unscaled)")]
    [Tooltip("Fade from black into the live menu scene at the very start.")]
    [SerializeField] private float fadeFromBlackDuration = 1.5f;
    [Tooltip("Backdrop alpha kept behind the logos (0 = scene fully visible; raise slightly to help logo legibility).")]
    [Range(0f, 1f)]
    [SerializeField] private float logoBackdropAlpha = 0f;
    [Tooltip("Pause after the fade from black before the first logo starts.")]
    [SerializeField] private float delayBeforeLogos = 0.5f;
    [Tooltip("Fade-in for the first logo. Longer than the others so it eases in gently right after the fade from black.")]
    [SerializeField] private float firstLogoFadeInDuration = 2f;
    [SerializeField] private float logoFadeInDuration = 1f;
    [SerializeField] private float logoHoldDuration = 1.5f;
    [SerializeField] private float logoFadeOutDuration = 1f;
    [SerializeField] private float delayBetweenLogos = 0.4f;
    [Tooltip("Main menu UI fade-in after the last logo.")]
    [SerializeField] private float menuFadeInDuration = 1.5f;

    [Header("Hold To Skip")]
    [Tooltip("Allow holding any key, mouse button, or gamepad face/Start button to skip the current logo.")]
    [SerializeField] private bool allowSkip = true;
    [Tooltip("How long the input must be held to skip the current logo.")]
    [SerializeField] private float holdToSkipDuration = 0.75f;
    [Tooltip("How long the current logo takes to fade out once skipped.")]
    [SerializeField] private float skipLogoFadeOutDuration = 0.3f;
    [Tooltip("Optional prompt (e.g. \"Hold to skip\") shown while the skip input is held.")]
    [SerializeField] private CanvasGroup skipPrompt;
    [Tooltip("Optional fill bar. Its localScale.x is driven 0-1 by hold progress (pivot should be on the left).")]
    [SerializeField] private RectTransform skipProgressFill;
    [SerializeField] private float skipPromptFadeSpeed = 6f;

    /// <summary>True from the moment the splash starts until the main menu UI has fully faded in.</summary>
    public static bool IsPlaying { get; private set; }

    /// <summary>
    /// True from the splash's Awake (before any Start) until it finishes, when the splash is going
    /// to play this scene load. Lets other systems defer menu-only setup until the splash ends.
    /// </summary>
    public static bool WillPlayThisLoad { get; private set; }

    /// <summary>Raised when the main menu UI starts fading in after the logos. Arg: fade duration.</summary>
    public static event Action<float> MenuFadeInStarted;

    /// <summary>Upper bound on the per-frame time step used by fades, so startup/loading hitches
    /// can't jump a fade ahead (which made the first logo appear to pop in).</summary>
    private const float MaxFadeStep = 1f / 30f;

    private static bool _hasPlayedThisSession;
    private static MainMenuSplashScreen _instance;

    private CanvasGroup _menuGroup;
    private Action _onLogosFinished;
    private Action _onComplete;
    private Coroutine _routine;

    private bool _canSkip;
    private bool _skipRequested;
    private bool _waitForRelease;
    private float _holdTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _hasPlayedThisSession = false;
        IsPlaying = false;
        WillPlayThisLoad = false;
        MenuFadeInStarted = null;
        _instance = null;
    }

    /// <summary>
    /// Cancels the splash for this load (and the rest of the session) when the main menu is
    /// bypassed, e.g. the debug "Game Start Point" window or cutscene mode. Safe to call from any
    /// Awake: if the splash has already covered the screen it is hidden immediately; otherwise it
    /// deactivates itself in its own Awake.
    /// </summary>
    public static void Suppress()
    {
        _hasPlayedThisSession = true;
        WillPlayThisLoad = false;

        if (_instance != null)
            _instance.HideImmediately();
    }

    private void Awake()
    {
        _instance = this;

        if (_hasPlayedThisSession)
        {
            WillPlayThisLoad = false;
            gameObject.SetActive(false);
            return;
        }

        WillPlayThisLoad = true;

        // Cover the screen immediately so nothing flashes before the fade from black.
        if (rootGroup != null)
        {
            rootGroup.alpha = 1f;
            rootGroup.blocksRaycasts = true;
        }

        if (backgroundGroup != null)
        {
            backgroundGroup.alpha = 1f;

            // The backdrop is the splash's input blocker. With cullTransparentMesh on, a fully
            // transparent backdrop gets culled and stops catching raycasts, so keep it unculled.
            foreach (CanvasRenderer cr in backgroundGroup.GetComponentsInChildren<CanvasRenderer>(true))
                cr.cullTransparentMesh = false;
        }

        foreach (CanvasGroup logo in logos)
        {
            if (logo != null)
                logo.alpha = 0f;
        }

        if (skipPrompt != null)
            skipPrompt.alpha = 0f;

        SetSkipProgress(0f);
    }

    private void Update()
    {
        if (!IsPlaying)
            return;

        UpdateHoldToSkip();
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;

        IsPlaying = false;
    }

    /// <summary>
    /// Plays the splash if it hasn't played yet this session. <paramref name="menuGroup"/> (the
    /// main menu UI) is hidden immediately and faded in after the last logo.
    /// <paramref name="onLogosFinished"/> fires once the last logo has faded out, right as the
    /// menu UI starts fading in. <paramref name="onComplete"/> fires once the menu UI is fully
    /// visible and input has been re-enabled.
    /// </summary>
    /// <returns>False if the splash was skipped (already played this session); callers should
    /// then show the main menu immediately.</returns>
    public bool TryPlay(CanvasGroup menuGroup, Action onLogosFinished, Action onComplete)
    {
        if (_hasPlayedThisSession || rootGroup == null || backgroundGroup == null)
        {
            WillPlayThisLoad = false;
            gameObject.SetActive(false);
            return false;
        }

        _hasPlayedThisSession = true;
        IsPlaying = true;
        _menuGroup = menuGroup;
        _onLogosFinished = onLogosFinished;
        _onComplete = onComplete;

        if (_menuGroup != null)
            _menuGroup.alpha = 0f;

        gameObject.SetActive(true);
        if (rootGroup != null)
            rootGroup.blocksRaycasts = true;
        ClearSelection();

        _routine = StartCoroutine(PlaySequence());
        return true;
    }

    /// <summary>Immediately ends the splash without invoking callbacks (e.g. debug skip-to-game).</summary>
    public void HideImmediately()
    {
        _hasPlayedThisSession = true;
        if (_routine != null)
            StopCoroutine(_routine);

        if (_menuGroup != null)
            _menuGroup.alpha = 1f;

        _onLogosFinished = null;
        _onComplete = null;
        Finish();
    }

    // ---------------------------------------------------------------------------
    // Sequence
    // ---------------------------------------------------------------------------

    private IEnumerator PlaySequence()
    {
        // 1. Fade from black into the live menu scene.
        yield return Fade(backgroundGroup, logoBackdropAlpha, fadeFromBlackDuration, interruptible: false);
        yield return Wait(delayBeforeLogos);

        // 2. Logos over the live scene.
        for (int i = 0; i < logos.Length; i++)
        {
            CanvasGroup logo = logos[i];
            if (logo == null)
                continue;

            _skipRequested = false;
            _canSkip = true;

            float fadeIn = i == 0 ? firstLogoFadeInDuration : logoFadeInDuration;
            yield return Fade(logo, 1f, fadeIn, interruptible: true);

            float held = 0f;
            while (held < logoHoldDuration && !_skipRequested)
            {
                held += FadeStep();
                yield return null;
            }

            if (!_skipRequested)
                yield return Fade(logo, 0f, logoFadeOutDuration, interruptible: true);

            if (_skipRequested)
                yield return Fade(logo, 0f, skipLogoFadeOutDuration, interruptible: false);

            _canSkip = false;

            if (i < logos.Length - 1)
                yield return Wait(delayBetweenLogos);
        }

        // 3. Main menu UI fades in (and any remaining backdrop clears).
        // The menu is clickable as soon as it starts appearing.
        _onLogosFinished?.Invoke();
        ReleaseInputToMenu();
        MenuFadeInStarted?.Invoke(menuFadeInDuration);

        if (backgroundGroup.alpha > 0f)
            StartCoroutine(Fade(backgroundGroup, 0f, menuFadeInDuration, interruptible: false));

        if (_menuGroup != null)
            yield return Fade(_menuGroup, 1f, menuFadeInDuration, interruptible: false);
        else
            yield return Wait(menuFadeInDuration);

        Finish();
        _onComplete?.Invoke();
    }

    /// <summary>Fades <paramref name="group"/> to <paramref name="target"/> with a smooth ease,
    /// stopping early if interruptible and a skip is requested.</summary>
    private IEnumerator Fade(CanvasGroup group, float target, float duration, bool interruptible)
    {
        float start = group.alpha;
        if (duration <= 0f)
        {
            group.alpha = target;
            yield break;
        }

        float t = 0f;
        while (t < 1f)
        {
            if (interruptible && _skipRequested)
                yield break;

            t = Mathf.Min(1f, t + FadeStep() / duration);
            group.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
    }

    private static IEnumerator Wait(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += FadeStep();
            yield return null;
        }
    }

    private static float FadeStep() => Mathf.Min(Time.unscaledDeltaTime, MaxFadeStep);

    private void Finish()
    {
        ReleaseInputToMenu();
        WillPlayThisLoad = false;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Stops the splash from absorbing input and makes the menu UI interactable. Called when the
    /// menu starts fading in (and again by <see cref="Finish"/>; idempotent).
    /// </summary>
    private void ReleaseInputToMenu()
    {
        IsPlaying = false;
        _canSkip = false;

        if (rootGroup != null)
            rootGroup.blocksRaycasts = false;

        if (skipPrompt != null)
            skipPrompt.alpha = 0f;
        SetSkipProgress(0f);

        if (_menuGroup != null)
        {
            _menuGroup.interactable = true;
            _menuGroup.blocksRaycasts = true;
        }
    }

    private void ClearSelection()
    {
        if (eventSystem != null && eventSystem.isActiveAndEnabled)
            eventSystem.SetSelectedGameObject(null);
    }

    // ---------------------------------------------------------------------------
    // Hold To Skip
    // ---------------------------------------------------------------------------

    private void UpdateHoldToSkip()
    {
        bool held = allowSkip && SkipInputHeld();

        if (!held)
        {
            // Releasing re-arms skipping and resets progress.
            _waitForRelease = false;
            _holdTime = 0f;
        }
        else if (_canSkip && !_skipRequested && !_waitForRelease)
        {
            _holdTime += Time.unscaledDeltaTime;
            if (_holdTime >= holdToSkipDuration)
            {
                _skipRequested = true;
                _waitForRelease = true;
                _holdTime = 0f;
            }
        }
        else
        {
            _holdTime = 0f;
        }

        float progress = holdToSkipDuration > 0f ? Mathf.Clamp01(_holdTime / holdToSkipDuration) : 0f;
        SetSkipProgress(progress);

        if (skipPrompt != null)
        {
            float targetAlpha = _holdTime > 0f ? 1f : 0f;
            skipPrompt.alpha = Mathf.MoveTowards(skipPrompt.alpha, targetAlpha, skipPromptFadeSpeed * Time.unscaledDeltaTime);
        }
    }

    private void SetSkipProgress(float progress)
    {
        if (skipProgressFill == null)
            return;

        Vector3 scale = skipProgressFill.localScale;
        scale.x = progress;
        skipProgressFill.localScale = scale;
    }

    private static bool SkipInputHeld()
    {
        Keyboard kb = Keyboard.current;
        if (kb != null && kb.anyKey.isPressed)
            return true;

        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed))
            return true;

        Gamepad gp = Gamepad.current;
        if (gp != null && (gp.buttonSouth.isPressed || gp.buttonEast.isPressed ||
                           gp.buttonNorth.isPressed || gp.buttonWest.isPressed ||
                           gp.startButton.isPressed))
            return true;

        return false;
    }
}
