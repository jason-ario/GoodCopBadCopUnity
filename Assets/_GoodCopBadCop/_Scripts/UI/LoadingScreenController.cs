using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the standalone Loading scene: asynchronously loads the target scene in the
/// background, smooths the reported progress into the bar/percent label, and only
/// activates the loaded scene once the load has finished AND a minimum display time
/// has elapsed (avoids a jarring instant flash on fast loads).
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

    private void Start()
    {
        StartCoroutine(LoadSceneRoutine());
    }

    private IEnumerator LoadSceneRoutine()
    {
        float startTime = Time.unscaledTime;

        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneToLoad, LoadSceneMode.Single);
        operation.allowSceneActivation = false;

        float displayedProgress = 0f;
        SetProgress(0f);

        while (!operation.isDone)
        {
            // Unity reports 0->0.9 while loading, then holds at 0.9 until activation is allowed.
            float targetProgress = Mathf.Clamp01(operation.progress / 0.9f);
            displayedProgress = Mathf.MoveTowards(displayedProgress, targetProgress, fillSpeed * Time.unscaledDeltaTime);
            SetProgress(displayedProgress);

            bool loadReady = operation.progress >= 0.9f;
            bool minTimeElapsed = Time.unscaledTime - startTime >= minimumDisplayTime;
            bool barFilled = displayedProgress >= 0.999f;

            if (loadReady && minTimeElapsed && barFilled && !operation.allowSceneActivation)
            {
                SetProgress(1f);
                operation.allowSceneActivation = true;
            }

            yield return null;
        }
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
