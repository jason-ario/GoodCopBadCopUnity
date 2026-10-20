using System.Collections;
using GoodCopBadCop.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the standalone Loading scene: asynchronously loads the target scene in the
/// background and smooths the reported progress into the bar/percent label.
/// The bar is split into phases so it never sits at 100% during the activation stall:
///   1. Background load (+ optional shader warmup) fills up to <see cref="preActivationShare"/>.
///   2. Activation (Awake/OnEnable of every object in the target scene) is a main-thread stall
///      that can't animate, so the bar holds below 100% while it happens.
///   3. The overlay (this object + the loading Canvas) is moved to DontDestroyOnLoad so it
///      survives the Single-mode scene swap, keeps covering the first Start()/first-render
///      hitches of the new scene while the bar fills to 100%, then fades out and destroys itself.
/// </summary>
public class LoadingScreenController : MonoBehaviour
{
    [SerializeField] private string sceneToLoad = "Main";
    [SerializeField] private Slider progressSlider;
    [SerializeField] private TextMeshProUGUI percentLabel;

    [Tooltip("Minimum time (seconds) the loading screen stays visible, even if the scene loads instantly.")]
    [SerializeField] private float minimumDisplayTime = 1.25f;

    [Tooltip("How quickly the displayed progress bar catches up to the real load progress (higher = snappier).")]
    [SerializeField] private float fillSpeed = 2.5f;

    [Tooltip("Optional: pre-builds traced shaders/PSOs while the scene loads, so they don't hitch in gameplay.")]
    [SerializeField] private ShaderWarmupRunner shaderWarmup;

    [Tooltip("Share of the pre-activation bar given to the shader warmup when there is warmup work to do.")]
    [SerializeField, Range(0f, 1f)] private float warmupProgressWeight = 0.4f;

    [Header("Activation Phase")]
    [Tooltip("Share of the bar filled by the background load before scene activation. The rest fills after the new scene has activated, so the bar never sits at 100% during the activation stall.")]
    [SerializeField, Range(0.1f, 0.95f)] private float preActivationShare = 0.7f;

    [Tooltip("Keep the overlay up for at least this long after the new scene activates, to hide its first Start()/first-render hitches.")]
    [SerializeField, Min(0f)] private float postActivationHoldTime = 0.6f;

    [Tooltip("Keep the overlay up for at least this many rendered frames after activation.")]
    [SerializeField, Min(1)] private int postActivationFrames = 5;

    [Tooltip("Fade-out duration (seconds) of the overlay once loading is complete.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.3f;

    [Tooltip("The loading overlay root kept alive across the scene swap. Defaults to the Canvas that owns the progress bar.")]
    [SerializeField] private Canvas overlayCanvas;

    [Tooltip("Largest per-frame delta used to advance the bar, so a hitch frame doesn't make it jump.")]
    [SerializeField, Min(0.005f)] private float maxBarStepDelta = 1f / 30f;

    [Header("Load Speed")]
    [Tooltip("Lets the async load use far more main-thread time per frame (High = ~50 ms vs ~4 ms default). Restored once the target scene has activated.")]
    [SerializeField] private bool boostLoadingPriority = true;

    [Tooltip("Per-frame milliseconds for uploading textures/meshes to the GPU while loading.")]
    [SerializeField, Range(1, 33)] private int loadingAsyncUploadTimeSlice = 8;

    [Tooltip("Async upload ring buffer size (MB) while loading. Larger = fewer stalls on big textures.")]
    [SerializeField, Range(2, 2047)] private int loadingAsyncUploadBufferSize = 128;

    private ThreadPriority _previousLoadingPriority;
    private int _previousUploadTimeSlice;
    private int _previousUploadBufferSize;
    private bool _boostApplied;
    private float _displayedProgress;

    private void Start()
    {
        ApplyLoadBoost();
        StartCoroutine(LoadSceneRoutine());
    }

    private void OnDestroy()
    {
        // Safety net; normally restored as soon as the target scene finishes activating.
        RestoreLoadBoost();
    }

    private void ApplyLoadBoost()
    {
        if (!boostLoadingPriority || _boostApplied)
        {
            return;
        }

        _previousLoadingPriority = Application.backgroundLoadingPriority;
        _previousUploadTimeSlice = QualitySettings.asyncUploadTimeSlice;
        _previousUploadBufferSize = QualitySettings.asyncUploadBufferSize;

        Application.backgroundLoadingPriority = ThreadPriority.High;
        QualitySettings.asyncUploadTimeSlice = Mathf.Max(_previousUploadTimeSlice, loadingAsyncUploadTimeSlice);
        QualitySettings.asyncUploadBufferSize = Mathf.Max(_previousUploadBufferSize, loadingAsyncUploadBufferSize);
        _boostApplied = true;
    }

    private void RestoreLoadBoost()
    {
        if (!_boostApplied)
        {
            return;
        }

        Application.backgroundLoadingPriority = _previousLoadingPriority;
        QualitySettings.asyncUploadTimeSlice = _previousUploadTimeSlice;
        QualitySettings.asyncUploadBufferSize = _previousUploadBufferSize;
        _boostApplied = false;
    }

    private IEnumerator LoadSceneRoutine()
    {
        float startTime = Time.unscaledTime;

        bool hasWarmup = shaderWarmup != null && shaderWarmup.HasWork;
        if (shaderWarmup != null)
        {
            StartCoroutine(shaderWarmup.Run());
        }

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
        operation.allowSceneActivation = false;

        _displayedProgress = 0f;
        SetProgress(0f);

        // Phase 1: background load (+ warmup) fills the bar up to preActivationShare.
        while (!operation.allowSceneActivation)
        {
            // Unity reports 0->0.9 while loading, then holds at 0.9 until activation is allowed.
            float loadProgress = Mathf.Clamp01(operation.progress / 0.9f);
            float phaseProgress = hasWarmup
                ? Mathf.Lerp(loadProgress, shaderWarmup.Progress, warmupProgressWeight)
                : loadProgress;
            float targetProgress = phaseProgress * preActivationShare;
            AdvanceBar(targetProgress);

            bool warmupDone = shaderWarmup == null || shaderWarmup.IsDone;
            bool loadReady = operation.progress >= 0.9f && warmupDone;
            bool minTimeElapsed = Time.unscaledTime - startTime >= minimumDisplayTime;
            bool barCaughtUp = _displayedProgress >= preActivationShare - 0.001f;

            if (loadReady && minTimeElapsed && barCaughtUp)
            {
                KeepOverlayAcrossSceneSwap();
                operation.allowSceneActivation = true;
            }

            yield return null;
        }

        // Phase 2: activation stall. The main thread is blocked running Awake/OnEnable for the
        // target scene, so the bar can't animate here; it holds below 100% instead of lying.
        while (!operation.isDone)
        {
            yield return null;
        }

        RestoreLoadBoost();

        // Phase 3: the new scene is live. Keep the overlay up through its first Start() calls and
        // first-render hitches while the bar fills the remaining share, then fade out.
        float activatedTime = Time.unscaledTime;
        int framesSinceActivation = 0;
        float remainingShare = 1f - preActivationShare;
        float tailSpeed = remainingShare / Mathf.Max(0.05f, postActivationHoldTime);

        while (_displayedProgress < 0.999f || framesSinceActivation < postActivationFrames)
        {
            float t = postActivationHoldTime > 0f
                ? Mathf.Clamp01((Time.unscaledTime - activatedTime) / postActivationHoldTime)
                : 1f;
            float targetProgress = Mathf.Lerp(preActivationShare, 1f, t);
            float dt = Mathf.Min(Time.unscaledDeltaTime, maxBarStepDelta);
            _displayedProgress = Mathf.MoveTowards(_displayedProgress, targetProgress, Mathf.Max(fillSpeed, tailSpeed) * dt);
            SetProgress(_displayedProgress);

            framesSinceActivation++;
            yield return null;
        }

        SetProgress(1f);
        yield return FadeOutAndDestroy();
    }

    private void AdvanceBar(float targetProgress)
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, maxBarStepDelta);
        _displayedProgress = Mathf.MoveTowards(_displayedProgress, targetProgress, fillSpeed * dt);
        SetProgress(_displayedProgress);
    }

    /// <summary>
    /// Moves the loader and overlay canvas to DontDestroyOnLoad so they survive the Single-mode
    /// swap. The target scene still loads as Single (active scene, sceneLoaded mode, lighting
    /// and reload-by-active-scene-path behaviour are all unchanged).
    /// </summary>
    private void KeepOverlayAcrossSceneSwap()
    {
        Canvas canvas = ResolveOverlayCanvas();
        if (canvas != null)
        {
            DontDestroyOnLoad(canvas.transform.root.gameObject);
        }

        DontDestroyOnLoad(transform.root.gameObject);
    }

    private Canvas ResolveOverlayCanvas()
    {
        if (overlayCanvas != null)
        {
            return overlayCanvas;
        }

        if (progressSlider != null)
        {
            overlayCanvas = progressSlider.GetComponentInParent<Canvas>();
        }

        if (overlayCanvas == null && percentLabel != null)
        {
            overlayCanvas = percentLabel.GetComponentInParent<Canvas>();
        }

        return overlayCanvas;
    }

    private IEnumerator FadeOutAndDestroy()
    {
        Canvas canvas = ResolveOverlayCanvas();
        GameObject overlayRoot = canvas != null ? canvas.transform.root.gameObject : null;

        if (overlayRoot != null && fadeOutDuration > 0f)
        {
            CanvasGroup group = overlayRoot.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = overlayRoot.AddComponent<CanvasGroup>();
            }

            group.blocksRaycasts = false;
            group.interactable = false;

            float elapsed = 0f;
            while (elapsed < fadeOutDuration)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, maxBarStepDelta);
                group.alpha = 1f - Mathf.Clamp01(elapsed / fadeOutDuration);
                yield return null;
            }
        }

        if (overlayRoot != null && overlayRoot != transform.root.gameObject)
        {
            Destroy(overlayRoot);
        }

        Destroy(transform.root.gameObject);
    }

    private void SetProgress(float value)
    {
        if (progressSlider != null)
        {
            progressSlider.value = value;
        }

        if (percentLabel != null)
        {
            percentLabel.text = Mathf.RoundToInt(value * 100f) + "%";
        }
    }
}
