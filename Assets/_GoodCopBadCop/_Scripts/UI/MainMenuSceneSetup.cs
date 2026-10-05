using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Disables the post-processing vignette and the Breakable Glass object while the main menu
/// is active, then explicitly re-enables both when the game starts.
///
/// If the <see cref="MainMenuSplashScreen"/> is playing, the vignette stays on during the logos
/// and fades out alongside the main menu UI fade-in instead of switching off at load.
/// </summary>
public class MainMenuSceneSetup : MonoBehaviour
{
    [Header("Post Processing")]
    [SerializeField] private Volume postProcessingVolume;

    [Header("Scene Objects")]
    [SerializeField] private GameObject breakableGlass;

    private Vignette _vignette;
    private float _vignetteIntensity;
    private Tween _vignetteFade;
    private bool _waitingForSplash;

    private void Start()
    {
        CacheVignette();

        if (MainMenuSplashScreen.WillPlayThisLoad)
        {
            // Keep the vignette on under the splash logos; fade it with the menu fade-in.
            _waitingForSplash = true;
            MainMenuSplashScreen.MenuFadeInStarted += OnSplashMenuFadeInStarted;
        }
        else
        {
            DisableVignette();
        }

        DisableBreakableGlass();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStart += OnGameStart;
        }
        else
        {
            Debug.LogWarning("[MainMenuSceneSetup] GameManager.Instance is null on Start.");
        }
    }

    private void OnDestroy()
    {
        if (_waitingForSplash)
            MainMenuSplashScreen.MenuFadeInStarted -= OnSplashMenuFadeInStarted;

        _vignetteFade?.Kill();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStart -= OnGameStart;
        }
    }

    // ---------------------------------------------------------------------------
    // Main Menu State
    // ---------------------------------------------------------------------------

    private void CacheVignette()
    {
        if (postProcessingVolume == null)
        {
            Debug.LogWarning("[MainMenuSceneSetup] postProcessingVolume is not assigned.");
            return;
        }

        // Use volume.profile (not sharedProfile) to get a runtime instance, avoiding
        // permanent modification of the shared Volume Profile asset.
        VolumeProfile profile = postProcessingVolume.profile;
        if (!profile.TryGet(out _vignette))
        {
            Debug.LogWarning("[MainMenuSceneSetup] No Vignette component found on the volume profile.");
            return;
        }

        _vignetteIntensity = _vignette.intensity.value;
    }

    private void DisableVignette()
    {
        if (_vignette == null)
            return;

        _vignette.active = false;
    }

    private void OnSplashMenuFadeInStarted(float duration)
    {
        _waitingForSplash = false;
        MainMenuSplashScreen.MenuFadeInStarted -= OnSplashMenuFadeInStarted;

        if (_vignette == null)
            return;

        _vignetteFade?.Kill();
        _vignetteFade = DOTween.To(
                () => _vignette.intensity.value,
                v => _vignette.intensity.value = v,
                0f,
                duration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                DisableVignette();
                // Restore the authored intensity so re-enabling at game start looks correct.
                _vignette.intensity.value = _vignetteIntensity;
            });
    }

    private void DisableBreakableGlass()
    {
        if (breakableGlass == null)
        {
            Debug.LogWarning("[MainMenuSceneSetup] breakableGlass is not assigned.");
            return;
        }

        breakableGlass.SetActive(false);
    }

    // ---------------------------------------------------------------------------
    // Gameplay State
    // ---------------------------------------------------------------------------

    private void OnGameStart()
    {
        GameManager.Instance.OnGameStart -= OnGameStart;

        EnableVignette();
        EnableBreakableGlass();
    }

    private IEnumerator RefreshGlassFromSaveNextFrame()
    {
        yield return null;
        BreakableGlassController.Instance?.RefreshFromSave();
    }

    private void EnableVignette()
    {
        if (_vignette == null)
            return;

        if (_waitingForSplash)
        {
            _waitingForSplash = false;
            MainMenuSplashScreen.MenuFadeInStarted -= OnSplashMenuFadeInStarted;
        }

        _vignetteFade?.Kill();
        _vignette.intensity.value = _vignetteIntensity;
        _vignette.active = true;
    }

    private void EnableBreakableGlass()
    {
        if (breakableGlass == null)
        {
            Debug.LogWarning("[MainMenuSceneSetup] breakableGlass is not assigned.");
            return;
        }

        breakableGlass.SetActive(true);

        // The BreakableGlassController.Start() coroutine may have been interrupted while
        // Breakable Glass was inactive during the main menu. RefreshFromSave() re-runs the
        // save-state restore (guarded against double-execution) with a one-frame delay to
        // match the original coroutine timing and ensure NetworkManager is ready.
        StartCoroutine(RefreshGlassFromSaveNextFrame());
    }
}
