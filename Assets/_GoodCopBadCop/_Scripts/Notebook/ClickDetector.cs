using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ClickDetector : MonoBehaviour
{
    [SerializeField] private Camera renderCamera;
    [SerializeField] private RawImage cameraImage; // the UI element showing the render texture
    
    void Update()
    {
        if (renderCamera == null || cameraImage == null)
        {
            Debug.LogError("Missing renderCamera or cameraImage reference.");
            renderCamera = PlayerInstance.Instance.GetCamera();
            cameraImage = UIController.Instance.GetCameraImage();
            return;
        }
        
        if (!Input.GetMouseButtonDown(0))
            return;

        RectTransform rectTransform = cameraImage.rectTransform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                Input.mousePosition,
                null, // use null for screen space overlay canvas
                out Vector2 localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;

        // Convert local point to 0-1 UV inside the RawImage
        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        // Reject clicks outside the image
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return;

        // Convert to render camera pixel coordinates
        Vector3 cameraScreenPoint = new Vector3(
            u * renderCamera.pixelWidth,
            v * renderCamera.pixelHeight,
            0f
        );

        Ray ray = renderCamera.ScreenPointToRay(cameraScreenPoint);
        Debug.DrawRay(ray.origin, ray.direction * 100f, Color.red, 2f);

        if (Physics.Raycast(ray, out RaycastHit hit, 100f, ~0, QueryTriggerInteraction.Collide))
        {
            Debug.Log("Hit: " + hit.collider.name);
            hit.collider.GetComponentInParent<IClickable>()?.OnClick();
        }
    }
}