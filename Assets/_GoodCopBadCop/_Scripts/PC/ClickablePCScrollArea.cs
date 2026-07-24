using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Allows click-and-drag scrolling directly on a scroll view's viewport/content area
/// (as opposed to only on the scrollbar handle/track). Attach this to the Viewport
/// GameObject of a Scroll View alongside a RectMask2D/Mask, and wire up the Scrollbar
/// that the corresponding ScrollRect is bound to.
/// </summary>
public class ClickablePCScrollArea : ClickablePCElement, IPCDraggable
{
    [Header("References")]
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private RectTransform viewportRect;
    [SerializeField] private RectTransform contentRect;
    [SerializeField] private RectTransform cursorHotspot;

    [Header("Settings")]
    [SerializeField] private bool vertical = true;
    [SerializeField] private bool invert = true;

    private bool isDragging;
    private Vector2 lastCursorLocalPos;

    protected override void Awake()
    {
        base.Awake();
        SetFeedbackAnimationEnabled(false);
    }

    public override void OnClick()
    {
        // Dragging is started externally by the cursor controller once the drag
        // threshold is exceeded. A plain click on empty space does nothing.
    }

    public void BeginDrag()
    {
        if (scrollbar == null || viewportRect == null || contentRect == null || cursorHotspot == null)
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
        if (!isDragging || scrollbar == null || viewportRect == null || contentRect == null || cursorHotspot == null)
            return;

        Vector2 currentCursorLocalPos = GetCursorLocalPosition();
        Vector2 localDelta = currentCursorLocalPos - lastCursorLocalPos;
        lastCursorLocalPos = currentCursorLocalPos;

        float axisDelta = vertical ? localDelta.y : localDelta.x;

        float contentSize = vertical ? contentRect.rect.height : contentRect.rect.width;
        float viewportSize = vertical ? viewportRect.rect.height : viewportRect.rect.width;
        float scrollableSize = Mathf.Max(1f, contentSize - viewportSize);

        float normalizedDelta = axisDelta / scrollableSize;

        if (invert)
            normalizedDelta = -normalizedDelta;

        scrollbar.value = Mathf.Clamp01(scrollbar.value + normalizedDelta);
    }

    private Vector2 GetCursorLocalPosition()
    {
        return viewportRect.InverseTransformPoint(cursorHotspot.position);
    }
}
