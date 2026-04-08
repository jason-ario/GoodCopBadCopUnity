using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class LowFPSSecurityCamera : MonoBehaviour
{
    [Header("Timing")]
    [Min(0.1f)] public float updatesPerSecond = 5f;
    [Tooltip("Adds a small startup offset so multiple cameras don't all render on the same frame.")]
    public float startOffsetSeconds = 0f;

    [Header("Optimization")]
    public bool onlyRenderWhenVisible = true;
    public Renderer monitorRenderer;
    public bool skipRenderIfFrameIsHeavy = false;
    [Tooltip("If enabled, skip CCTV render when the current frame already exceeded this unscaled delta time.")]
    public float heavyFrameThresholdMs = 20f;

    private Camera _cam;
    private Coroutine _routine;

    private void Awake()
    {
        _cam = GetComponent<Camera>();

        // Prevent automatic every-frame rendering.
        _cam.enabled = false;
    }

    private void OnEnable()
    {
        _routine = StartCoroutine(RenderRoutine());
    }

    private void OnDisable()
    {
        if (_routine != null)
        {
            StopCoroutine(_routine);
            _routine = null;
        }
    }

    private IEnumerator RenderRoutine()
    {
        if (startOffsetSeconds > 0f)
            yield return new WaitForSecondsRealtime(startOffsetSeconds);

        float interval = 1f / updatesPerSecond;

        while (true)
        {
            // Wait until the next scheduled update.
            yield return new WaitForSecondsRealtime(interval);

            if (onlyRenderWhenVisible && monitorRenderer != null && !monitorRenderer.isVisible)
                continue;

            if (skipRenderIfFrameIsHeavy)
            {
                float frameMs = Time.unscaledDeltaTime * 1000f;
                if (frameMs > heavyFrameThresholdMs)
                    continue;
            }

            // Push the render to the next frame so it doesn't bunch up
            // with the frame that just finished doing gameplay work.
            yield return null;

            // Optionally wait until the end of that next frame, which can
            // make timing a little more stable in some projects.
            yield return new WaitForEndOfFrame();

            _cam.Render();
        }
    }

    public void ForceRenderNow()
    {
        if (_cam == null)
            _cam = GetComponent<Camera>();

        _cam.Render();
    }

    public void SetFPS(float fps)
    {
        updatesPerSecond = Mathf.Max(0.1f, fps);

        if (isActiveAndEnabled)
        {
            if (_routine != null)
                StopCoroutine(_routine);

            _routine = StartCoroutine(RenderRoutine());
        }
    }
}