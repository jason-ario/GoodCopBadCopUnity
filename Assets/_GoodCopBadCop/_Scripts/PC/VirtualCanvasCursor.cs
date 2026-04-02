using System;
using UnityEngine;

public class SimpleCanvasCursorFromMouseDelta : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform cursorRect;

    // NEW: optional precise click point
    [SerializeField] private RectTransform cursorHotspot;

    [Header("Movement")]
    [SerializeField] private float sensitivity = 1f;
    [SerializeField] private float xMargin;
    [SerializeField] private float yMargin;

    [Header("Input")]
    [SerializeField] private KeyCode clickKey = KeyCode.Mouse0;

    private Vector3 lastMousePosition;
    private Vector2 lastMouseDelta;

    private ClickablePCElement currentHoveredElement;
    private ClickablePCScrollbar currentDraggedScrollbar;

    private ClickablePCElement[] clickableElements = Array.Empty<ClickablePCElement>();
    private readonly Vector3[] cornersBuffer = new Vector3[4];

    private void Awake()
    {
        RefreshClickableElements();
        ResetMouseTracking();
    }

    private void OnEnable()
    {
        ResetState();
        RefreshClickableElements();
        ResetMouseTracking();
    }

    private void Update()
    {
        MoveCursor();
        UpdateHoveredElement();
        HandleClickDown();
        HandleDragging();
        HandleClickUp();
    }

    // ========================
    // Cursor Movement
    // ========================

    private void MoveCursor()
    {
        Vector3 currentMousePosition = Input.mousePosition;
        Vector3 mouseDelta = currentMousePosition - lastMousePosition;
        lastMousePosition = currentMousePosition;

        lastMouseDelta = new Vector2(mouseDelta.x, mouseDelta.y) * sensitivity;

        Vector2 pos = cursorRect.anchoredPosition + lastMouseDelta;

        Rect rect = canvasRect.rect;
        pos.x = Mathf.Clamp(pos.x, rect.xMin + xMargin, rect.xMax - xMargin);
        pos.y = Mathf.Clamp(pos.y, rect.yMin + yMargin, rect.yMax - yMargin);

        cursorRect.anchoredPosition = pos;
    }

    // ========================
    // Hover Detection
    // ========================

    private void UpdateHoveredElement()
    {
        if (canvasRect == null)
        {
            SetHoveredElement(null);
            return;
        }

        Vector2 cursorPoint = GetCursorPointInCanvasSpace();

        ClickablePCElement bestElement = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < clickableElements.Length; i++)
        {
            ClickablePCElement element = clickableElements[i];

            if (element == null || !element.gameObject.activeInHierarchy)
                continue;

            RectTransform elementRect = element.transform as RectTransform;
            if (elementRect == null)
                continue;

            Rect rect = GetRectInCanvasSpace(elementRect);

            if (!rect.Contains(cursorPoint))
                continue;

            int score = GetSortScore(elementRect);
            if (score > bestScore)
            {
                bestScore = score;
                bestElement = element;
            }
        }

        SetHoveredElement(bestElement);
    }

    private void SetHoveredElement(ClickablePCElement newHoveredElement)
    {
        if (newHoveredElement == currentHoveredElement)
            return;

        if (currentHoveredElement != null)
            currentHoveredElement.OnHoverExit();

        currentHoveredElement = newHoveredElement;

        if (currentHoveredElement != null)
            currentHoveredElement.OnHoverEnter();
    }

    // ========================
    // Click Handling
    // ========================

    private void HandleClickDown()
    {
        if (!Input.GetKeyDown(clickKey))
            return;

        if (currentHoveredElement == null)
            return;

        currentHoveredElement.OnClick();

        if (currentHoveredElement is ClickablePCScrollbar scrollbar)
        {
            currentDraggedScrollbar = scrollbar;
            currentDraggedScrollbar.BeginDrag();
        }
    }

    private void HandleDragging()
    {
        if (currentDraggedScrollbar == null)
            return;

        if (!Input.GetKey(clickKey))
            return;

        currentDraggedScrollbar.DragFromCursor();
    }

    private void HandleClickUp()
    {
        if (!Input.GetKeyUp(clickKey))
            return;

        EndDragging();
    }

    // ========================
    // Helpers
    // ========================

    private Vector2 GetCursorPointInCanvasSpace()
    {
        RectTransform source = cursorHotspot != null ? cursorHotspot : cursorRect;
        return canvasRect.InverseTransformPoint(source.position);
    }

    private Rect GetRectInCanvasSpace(RectTransform rt)
    {
        rt.GetWorldCorners(cornersBuffer);

        Vector2 bottomLeft = canvasRect.InverseTransformPoint(cornersBuffer[0]);
        Vector2 topRight = canvasRect.InverseTransformPoint(cornersBuffer[2]);

        return new Rect(bottomLeft, topRight - bottomLeft);
    }

    private int GetSortScore(RectTransform rt)
    {
        int depth = 0;
        Transform current = rt;

        while (current != null)
        {
            depth++;
            current = current.parent;
        }

        return depth * 1000 + rt.GetSiblingIndex();
    }

    private void EndDragging()
    {
        if (currentDraggedScrollbar != null)
        {
            currentDraggedScrollbar.EndDrag();
            currentDraggedScrollbar = null;
        }
    }

    private void ResetState()
    {
        SetHoveredElement(null);
        EndDragging();
    }

    public void RefreshClickableElements()
    {
        if (canvasRect == null)
        {
            clickableElements = Array.Empty<ClickablePCElement>();
            return;
        }

        clickableElements = canvasRect.GetComponentsInChildren<ClickablePCElement>(true);
    }

    public void SetScreenContent()
    {
        RefreshClickableElements();
        ResetState();
        ResetMouseTracking();
    }

    public void ResetMouseTracking()
    {
        lastMousePosition = Input.mousePosition;
        lastMouseDelta = Vector2.zero;
    }
}