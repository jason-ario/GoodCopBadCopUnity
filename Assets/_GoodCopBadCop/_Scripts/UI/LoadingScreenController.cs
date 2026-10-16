using System.Collections;
using GoodCopBadCop.Rendering;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Drives the standalone Loading scene: asynchronously loads the target scene in the
/// background, smooths the reported progress into the bar/percent label, and only
/// activates the loaded scene once the load has finished AND a minimum display time
/// has elapsed (avoids a jarring instant flash on fast loads). If a <see cref="ShaderWarmupRunner"/>
/// is assigned, activation also waits for the shader/PSO warmup and the bar includes its progress.
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

    [Tooltip("Share of the loading bar given to the shader warmup when there is warmup work to do.")]
    [SerializeField, Range(0f, 1f)] private float warmupProgressWeight = 0.4f;

    private void Start()
    {
        StartCoroutine(LoadSceneRoutine());
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

        float displayedProgress = 0f;
        SetProgress(0f);

        while (!operation.isDone)
        {
            // Unity reports 0->0.9 while loading, then holds at 0.9 until activation is allowed.
            float loadProgress = Mathf.Clamp01(operation.progress / 0.9f);
            float targetProgress = hasWarmup
                ? Mathf.Lerp(loadProgress, shaderWarmup.Progress, warmupProgressWeight)
                : loadProgress;
            displayedProgress = Mathf.MoveTowards(displayedProgress, targetProgress, fillSpeed * Time.unscaledDeltaTime);
            SetProgress(displayedProgress);

            bool warmupDone = shaderWarmup == null || shaderWarmup.IsDone;
            bool loadReady = operation.progress >= 0.9f && warmupDone;
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
