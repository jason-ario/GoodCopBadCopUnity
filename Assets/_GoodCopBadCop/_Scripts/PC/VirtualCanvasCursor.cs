using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class SimpleCanvasCursorFromMouseDelta : MonoBehaviour
{
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform cursorRect;
    [SerializeField] private float sensitivity = 1f;
    [SerializeField] private float xMargin;
    [SerializeField] private float yMargin;
    [SerializeField] private KeyCode clickKey = KeyCode.Mouse0;

    [SerializeField] private Canvas pcCanvas;
    [SerializeField] private Transform currentScreenContentRoot;

    private Vector3 lastMousePosition;
    private Vector2 lastMouseDelta;

    private ClickablePCElement currentHoveredElement;
    private ClickablePCScrollbar currentDraggedScrollbar;
    private ClickablePCElement[] clickablePCElements;

    private void Awake()
    {
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

    private void UpdateHoveredElement()
    {
        Rect cursorWorldRect = GetWorldRect(cursorRect);

        ClickablePCElement newHoveredElement = null;

        for (int i = 0; i < clickablePCElements.Length; i++)
        {
            ClickablePCElement element = clickablePCElements[i];

            if (element == null)
                continue;

            if (!element.gameObject.activeInHierarchy)
                continue;

            if (currentScreenContentRoot != null && !element.transform.IsChildOf(currentScreenContentRoot))
                continue;

            RectTransform elementRect = element.transform as RectTransform;
            if (elementRect == null)
                continue;

            Rect targetWorldRect = GetWorldRect(elementRect);

            if (cursorWorldRect.Overlaps(targetWorldRect))
            {
                newHoveredElement = element;
                break;
            }
        }

        if (newHoveredElement == currentHoveredElement)
            return;

        if (currentHoveredElement != null)
            currentHoveredElement.OnHoverExit();

        currentHoveredElement = newHoveredElement;

        if (currentHoveredElement != null)
            currentHoveredElement.OnHoverEnter();
    }

    private void HandleClickDown()
    {
        if (!Input.GetKeyDown(clickKey))
            return;

        if (currentHoveredElement == null)
            return;

        currentHoveredElement.OnClick();

        ClickablePCScrollbar scrollbar = currentHoveredElement as ClickablePCScrollbar;
        if (scrollbar != null)
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

    private void RefreshClickableElements()
    {
        if (pcCanvas == null)
        {
            clickablePCElements = Array.Empty<ClickablePCElement>();
            return;
        }

        clickablePCElements = pcCanvas.GetComponentsInChildren<ClickablePCElement>(true);
    }

    public void SetScreenContent(Transform newScreenContentRoot)
    {
        currentScreenContentRoot = newScreenContentRoot;
        RefreshAfterScreenChanged();
    }

    public void RefreshAfterScreenChanged()
    {
        ClearHover();
        EndDragging();
        RefreshClickableElements();
        ResetMouseTracking();
    }

    private void ClearHover()
    {
        if (currentHoveredElement != null)
        {
            currentHoveredElement.OnHoverExit();
            currentHoveredElement = null;
        }
    }

    private void EndDragging()
    {
        if (currentDraggedScrollbar != null)
        {
            currentDraggedScrollbar.EndDrag();
            currentDraggedScrollbar = null;
        }
    }

    private Rect GetWorldRect(RectTransform rectTransform)
    {
        Vector3[] corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);

        Vector2 min = corners[0];
        Vector2 max = corners[2];

        return new Rect(min, max - min);
    }

    public void ResetMouseTracking()
    {
        lastMousePosition = Input.mousePosition;
        lastMouseDelta = Vector2.zero;
    }
}