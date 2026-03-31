using UnityEngine;
using UnityEngine.UI;

public class ClickablePCScrollbar : ClickablePCElement
{
    [SerializeField] private Scrollbar scrollbar;
    [SerializeField] private RectTransform trackRect;
    [SerializeField] private RectTransform cursorRect;

    [SerializeField] private bool vertical = true;
    [SerializeField] private bool invert = false;

    private bool isDragging;

    public void BeginDrag()
    {
        isDragging = true;
    }

    public void EndDrag()
    {
        isDragging = false;
    }

    public void DragFromCursor()
    {
        if (!isDragging || scrollbar == null || trackRect == null || cursorRect == null)
            return;

        // Convert cursor into track local space
        Vector3 localCursor = trackRect.InverseTransformPoint(cursorRect.position);

        float normalized;

        if (vertical)
        {
            float min = -trackRect.rect.height * 0.5f;
            float max =  trackRect.rect.height * 0.5f;

            normalized = Mathf.InverseLerp(min, max, localCursor.y);
        }
        else
        {
            float min = -trackRect.rect.width * 0.5f;
            float max =  trackRect.rect.width * 0.5f;

            normalized = Mathf.InverseLerp(min, max, localCursor.x);
        }

        if (invert)
            normalized = 1f - normalized;

        scrollbar.value = normalized;
    }
}