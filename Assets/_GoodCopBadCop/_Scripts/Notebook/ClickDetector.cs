using UnityEngine;
using UnityEngine.UI;

public class ClickDetector : MonoBehaviour
{ 
    private Camera renderCamera;
    [SerializeField] private RawImage cameraImage; // the UI element showing the render texture

    private IHoverable _lastHoverable;

    void Update()
    {
        if (renderCamera == null)
        {
            if (PlayerInstance.Instance == null) return;
            renderCamera = PlayerInstance.Instance.GetCamera();
        }

        // When the cursor is hidden, clear any active hover and skip.
        if (!Cursor.visible)
        {
            ClearHover();
            return;
        }

        // Diegetic views (tool locker, electric panel, etc.) already raycast against the
        // same render camera and dispatch their own IClickable/IHoverable calls while open.
        // Without this guard, this detector's rect covers the full screen regardless of
        // whether the underlying RawImage is actually active, so it would fire a second,
        // redundant OnClick() alongside the diegetic view's own click handling — e.g.
        // double-toggling a CircuitSwitch back to its original state on every click.
        if (DiegeticViewController.IsAnyViewActive)
        {
            ClearHover();
            return;
        }

        Vector3? cameraScreenPoint = GetCameraScreenPoint();
        if (cameraScreenPoint == null)
        {
            ClearHover();
            return;
        }

        Ray ray = renderCamera.ScreenPointToRay(cameraScreenPoint.Value);
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide);

        // ── Hover ─────────────────────────────────────────────────────────────
        IHoverable hoverable = didHit ? hit.collider.GetComponentInParent<IHoverable>() : null;
        if (hoverable != _lastHoverable)
        {
            _lastHoverable?.OnHoverExit();
            hoverable?.OnHoverEnter();
            _lastHoverable = hoverable;
        }

        // ── Click ─────────────────────────────────────────────────────────────
        if (Input.GetMouseButtonDown(0) && didHit)
        {
            Debug.Log("Hit: " + hit.collider.name);
            hit.collider.GetComponentInParent<IClickable>()?.OnClick();
        }
    }

    private void ClearHover()
    {
        if (_lastHoverable == null) return;
        _lastHoverable.OnHoverExit();
        _lastHoverable = null;
    }

    /// <summary>
    /// Maps the current mouse position through the <see cref="cameraImage"/> RectTransform
    /// to camera pixel coordinates. Returns null if the cursor is outside the image.
    /// </summary>
    private Vector3? GetCameraScreenPoint()
    {
        RectTransform rectTransform = cameraImage.rectTransform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                Input.mousePosition,
                null, // screen space overlay canvas
                out Vector2 localPoint))
            return null;

        Rect rect = rectTransform.rect;
        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return null;

        return new Vector3(
            u * renderCamera.pixelWidth,
            v * renderCamera.pixelHeight,
            0f);
    }
}
