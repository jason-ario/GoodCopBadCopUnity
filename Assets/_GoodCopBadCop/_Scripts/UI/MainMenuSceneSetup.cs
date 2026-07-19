using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Disables the post-processing vignette and the Breakable Glass object while the main menu
/// is active, then explicitly re-enables both when the game starts.
/// </summary>
public class MainMenuSceneSetup : MonoBehaviour
{
    [Header("Post Processing")]
    [SerializeField] private Volume postProcessingVolume;

    [Header("Scene Objects")]
    [SerializeField] private GameObject breakableGlass;

    private Vignette _vignette;

    private void Start()
    {
        DisableVignette();
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnGameStart -= OnGameStart;
        }
    }

    // ---------------------------------------------------------------------------
    // Main Menu State
    // ---------------------------------------------------------------------------

    private void DisableVignette()
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

        _vignette.active = false;
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

    private void EnableVignette()
    {
        if (_vignette == null)
            return;

        _vignette.active = true;
    }

    private void EnableBreakableGlass()
    {
        if (breakableGlass == null)
            return;

        breakableGlass.SetActive(true);
    }
}
