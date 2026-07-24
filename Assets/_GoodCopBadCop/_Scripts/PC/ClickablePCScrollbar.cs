using UnityEngine;
using UnityEngine.UI;

public class ClickablePCScrollbar : ClickablePCElement, IPCDraggable
{
    [Header("References")]
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private RectTransform trackRect;
    [SerializeField] private RectTransform handleRect;
    [SerializeField] private RectTransform cursorHotspot;

    [Header("Settings")]
    [SerializeField] private bool vertical = true;
    [SerializeField] private bool invert = false;

    private bool isDragging;
    private Vector2 lastCursorLocalPos;

    protected override void Awake()
    {
        base.Awake();
        SetFeedbackAnimationEnabled(false);
    }

    public override void OnClick()
    {
        // Handle click itself does nothing special.
        // Dragging is started externally by the cursor controller.
    }

    public void BeginDrag()
    {
        if (scrollbar == null || trackRect == null || handleRect == null || cursorHotspot == null)
            return;

        isDragging = true;
        lastCursorLocalPos = GetCursorLocalPosition();
    }

    public void EndDrag()
    {
        isDragging = false;
    }

    public void DragFromCursorDelta()
    {
        if (!isDragging || scrollbar == null || trackRect == null || handleRect == null || cursorHotspot == null)
            return;

        Vector2 currentCursorLocalPos = GetCursorLocalPosition();
        Vector2 localDelta = currentCursorLocalPos - lastCursorLocalPos;
        lastCursorLocalPos = currentCursorLocalPos;

        float axisDelta = vertical ? localDelta.y : localDelta.x;

        float trackSize = vertical ? trackRect.rect.height : trackRect.rect.width;
        float handleSize = vertical ? handleRect.rect.height : handleRect.rect.width;
        float usableTrackSize = Mathf.Max(1f, trackSize - handleSize);

        float normalizedDelta = axisDelta / usableTrackSize;

        if (invert)
            normalizedDelta = -normalizedDelta;

        scrollbar.value = Mathf.Clamp01(scrollbar.value + normalizedDelta);
    }

    public void ScrollFromWheel(float wheelDelta)
    {
        if (scrollbar == null || Mathf.Approximately(wheelDelta, 0f))
            return;

        float normalizedDelta = wheelDelta * 0.08f;
        if (invert)
            normalizedDelta = -normalizedDelta;

        scrollbar.value = Mathf.Clamp01(scrollbar.value + normalizedDelta);
    }

    public void ResetToTop()
    {
        if (scrollbar == null)
            return;

        scrollbar.value = invert ? 0f : 1f;
    }

    public void ResetToBottom()
    {
        if (scrollbar == null)
            return;

        scrollbar.value = invert ? 1f : 0f;
    }

    public void JumpToCursorPosition()
    {
        if (scrollbar == null || trackRect == null || handleRect == null || cursorHotspot == null)
            return;

        Vector2 localCursor = GetCursorLocalPosition();

        float trackSize = vertical ? trackRect.rect.height : trackRect.rect.width;
        float handleSize = vertical ? handleRect.rect.height : handleRect.rect.width;

        float min = (-trackSize * 0.5f) + (handleSize * 0.5f);
        float max = (trackSize * 0.5f) - (handleSize * 0.5f);

        float axis = vertical ? localCursor.y : localCursor.x;
        float normalized = Mathf.InverseLerp(min, max, axis);

        if (invert)
            normalized = 1f - normalized;

        scrollbar.value = Mathf.Clamp01(normalized);
    }

    private Vector2 GetCursorLocalPosition()
    {
        return trackRect.InverseTransformPoint(cursorHotspot.position);
    }
}