using System;
using UnityEngine;
using UnityEngine.UI;

public class SimpleCanvasCursorFromMouseDelta : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform cursorRect;

    // optional precise click point
    [SerializeField] private RectTransform cursorHotspot;

    [Header("Movement")]
    [SerializeField] private float sensitivity = 1f;
    [SerializeField] private float axisDeltaMultiplier = 60f;
    [SerializeField] private float xMargin;
    [SerializeField] private float yMargin;

    [Header("Input")]
    [SerializeField] private KeyCode clickKey = KeyCode.Mouse0;

    [Header("Scroll Drag")]
    [Tooltip("Minimum cursor movement (in canvas units) before a press-and-drag over a scrollable area is treated as a drag instead of a click.")]
    [SerializeField] private float dragStartThreshold = 8f;

    private const float MinUsableSensitivity = 0.1f;
    private const float MinAxisDeltaMultiplier = 60f;

    private Vector3 lastMousePosition;
    private Vector2 lastMouseDelta;

    private ClickablePCElement currentHoveredElement;
    private IPCDraggable currentDraggedElement;

    private ClickablePCElement pendingClickElement;
    private IPCDraggable pendingDragCandidate;
    private Vector2 pendingDownCanvasPos;

    private ClickablePCElement[] clickableElements = Array.Empty<ClickablePCElement>();
    private readonly Vector3[] cornersBuffer = new Vector3[4];

    public Vector2 LastMouseDelta => lastMouseDelta;

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
        HandleScrollWheel();
        HandleClickDown();
        HandleDragging();
        HandleClickUp();
    }

    private void MoveCursor()
    {
        Vector3 currentMousePosition = Input.mousePosition;
        Vector2 axisDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        Vector2 positionDelta = currentMousePosition - lastMousePosition;
        lastMousePosition = currentMousePosition;

        float effectiveSensitivity = Mathf.Max(MinUsableSensitivity, sensitivity);
        lastMouseDelta = axisDelta.sqrMagnitude > 0f
            ? axisDelta * Mathf.Max(MinAxisDeltaMultiplier, axisDeltaMultiplier) * effectiveSensitivity
            : positionDelta * effectiveSensitivity;

        Vector2 pos = cursorRect.anchoredPosition + lastMouseDelta;

        Rect rect = canvasRect.rect;
        pos.x = Mathf.Clamp(pos.x, rect.xMin + xMargin, rect.xMax - xMargin);
        pos.y = Mathf.Clamp(pos.y, rect.yMin + yMargin, rect.yMax - yMargin);

        cursorRect.anchoredPosition = pos;
    }



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

            if (!IsElementVisibleAtPoint(elementRect, cursorPoint))
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

    private bool IsElementVisibleAtPoint(RectTransform elementRect, Vector2 canvasPoint)
    {
        Transform current = elementRect;

        while (current != null && current != canvasRect)
        {
            RectTransform currentRect = current as RectTransform;

            if (currentRect != null)
            {
                RectMask2D rectMask = current.GetComponent<RectMask2D>();
                if (rectMask != null)
                {
                    Rect maskRect = GetRectInCanvasSpace(currentRect);
                    if (!maskRect.Contains(canvasPoint))
                        return false;
                }

                Mask mask = current.GetComponent<Mask>();
                if (mask != null)
                {
                    Rect maskRect = GetRectInCanvasSpace(currentRect);
                    if (!maskRect.Contains(canvasPoint))
                        return false;
                }
            }

            current = current.parent;
        }

        return true;
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

    private void HandleScrollWheel()
    {
        float wheelDelta = Input.mouseScrollDelta.y;
        if (Mathf.Approximately(wheelDelta, 0f))
            return;

        ClickablePCScrollbar scrollbar = FindActiveScrollbar();
        if (scrollbar != null)
            scrollbar.ScrollFromWheel(wheelDelta);
    }

    private ClickablePCScrollbar FindActiveScrollbar()
    {
        for (int i = 0; i < clickableElements.Length; i++)
        {
            if (clickableElements[i] is ClickablePCScrollbar scrollbar
                && scrollbar.enabled
                && scrollbar.gameObject.activeInHierarchy)
            {
                return scrollbar;
            }
        }

        return null;
    }

    private void HandleClickDown()
    {
        if (!Input.GetKeyDown(clickKey))
            return;

        if (currentHoveredElement == null)
            return;

        if (currentHoveredElement is IPCDraggable directDraggable)
        {
            currentDraggedElement = directDraggable;
            currentDraggedElement.BeginDrag();
            return;
        }

        IPCDraggable ancestorDraggable = FindDraggableAncestor(currentHoveredElement);
        if (ancestorDraggable != null)
        {
            pendingClickElement = currentHoveredElement;
            pendingDragCandidate = ancestorDraggable;
            pendingDownCanvasPos = GetCursorPointInCanvasSpace();
            return;
        }

        currentHoveredElement.OnClick();
    }

    private void HandleDragging()
    {
        if (currentDraggedElement != null)
        {
            if (!Input.GetKey(clickKey))
                return;

            currentDraggedElement.DragFromCursorDelta();
            return;
        }

        if (pendingDragCandidate == null)
            return;

        if (!Input.GetKey(clickKey))
            return;

        Vector2 currentCanvasPos = GetCursorPointInCanvasSpace();
        if ((currentCanvasPos - pendingDownCanvasPos).sqrMagnitude < dragStartThreshold * dragStartThreshold)
            return;

        currentDraggedElement = pendingDragCandidate;
        pendingDragCandidate = null;
        pendingClickElement = null;
        currentDraggedElement.BeginDrag();
        currentDraggedElement.DragFromCursorDelta();
    }

    private void HandleClickUp()
    {
        if (!Input.GetKeyUp(clickKey))
            return;

        if (pendingClickElement != null)
        {
            pendingClickElement.OnClick();
            pendingClickElement = null;
            pendingDragCandidate = null;
        }

        EndDragging();
    }

    private IPCDraggable FindDraggableAncestor(ClickablePCElement element)
    {
        if (element == null)
            return null;

        Transform current = element.transform.parent;
        while (current != null && current != canvasRect)
        {
            IPCDraggable draggable = current.GetComponent<IPCDraggable>();
            if (draggable != null)
                return draggable;

            current = current.parent;
        }

        return null;
    }

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
        if (currentDraggedElement != null)
        {
            currentDraggedElement.EndDrag();
            currentDraggedElement = null;
        }
    }

    private void ResetState()
    {
        SetHoveredElement(null);
        pendingClickElement = null;
        pendingDragCandidate = null;
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