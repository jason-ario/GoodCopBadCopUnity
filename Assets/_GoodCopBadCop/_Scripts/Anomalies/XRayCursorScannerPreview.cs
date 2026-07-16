using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.XRay
{
    /// <summary>
    /// Play Mode-only cursor magnifier used by AnomalyPreviewWindow. It renders the selected
    /// suspect into an isolated X-ray camera and displays the matching screen crop in a square
    /// under the mouse pointer; the regular Game view remains untouched outside that square.
    /// </summary>
    public sealed class XRayCursorScannerPreview : MonoBehaviour
    {
        private const int IsolatedRenderLayer = 31;
        private const float ScopeSize = 230f;
        private const float BorderSize = 3f;

        private Camera _sourceCamera;
        private Camera _xRayCamera;
        private XRayAnatomyView _anatomyView;
        private GameObject _targetRoot;
        private RenderTexture _renderTexture;
        private Canvas _canvas;
        private RectTransform _scopeRect;
        private RawImage _scopeImage;

        public void Configure(Camera sourceCamera, GameObject targetRoot, XRayAnatomyView anatomyView)
        {
            _sourceCamera = sourceCamera;
            _targetRoot = targetRoot;
            _anatomyView = anatomyView;
            CreateVisuals();
            CreateCamera();
        }

        private void LateUpdate()
        {
            if (_sourceCamera == null || _targetRoot == null || _anatomyView == null)
            {
                SetScopeVisible(false);
                return;
            }

            bool isOverTarget = IsPointerOverTarget();
            SetScopeVisible(isOverTarget);
            if (!isOverTarget)
            {
                // RenderXRayTo restores its state after each capture. Keep this explicit cleanup
                // as a safety net so no anatomy primitive can remain in the regular Game view.
                _anatomyView.SetXRayVisible(false);
                return;
            }

            EnsureRenderTexture();
            if (_renderTexture == null)
                return;

            _xRayCamera.CopyFrom(_sourceCamera);
            _xRayCamera.enabled = false;
            _xRayCamera.transform.SetPositionAndRotation(_sourceCamera.transform.position, _sourceCamera.transform.rotation);
            _anatomyView.RenderXRayTo(_xRayCamera, _renderTexture, IsolatedRenderLayer);
            UpdateScopeCrop();
        }

        private void OnDestroy()
        {
            if (_anatomyView != null)
                _anatomyView.SetXRayVisible(false);

            if (_canvas != null)
                Destroy(_canvas.gameObject);
            if (_xRayCamera != null)
                Destroy(_xRayCamera.gameObject);
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }
        }

        private void CreateVisuals()
        {
            if (_canvas != null)
                return;

            GameObject canvasObject = new GameObject("[XRay Preview] Cursor Scope");
            canvasObject.hideFlags = HideFlags.DontSave;
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            GameObject borderObject = new GameObject("Scope Border");
            borderObject.transform.SetParent(canvasObject.transform, false);
            _scopeRect = borderObject.AddComponent<RectTransform>();
            _scopeRect.anchorMin = Vector2.zero;
            _scopeRect.anchorMax = Vector2.zero;
            _scopeRect.pivot = new Vector2(0.5f, 0.5f);
            _scopeRect.sizeDelta = new Vector2(ScopeSize + BorderSize * 2f, ScopeSize + BorderSize * 2f);
            borderObject.AddComponent<Image>().color = new Color(0.7f, 0.15f, 1f, 0.95f);

            GameObject imageObject = new GameObject("XRay Crop");
            imageObject.transform.SetParent(borderObject.transform, false);
            RectTransform imageRect = imageObject.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = new Vector2(BorderSize, BorderSize);
            imageRect.offsetMax = new Vector2(-BorderSize, -BorderSize);
            _scopeImage = imageObject.AddComponent<RawImage>();
            _scopeImage.raycastTarget = false;
            SetScopeVisible(false);
        }

        private void CreateCamera()
        {
            if (_xRayCamera != null)
                return;

            GameObject cameraObject = new GameObject("[XRay Preview] Render Camera")
            {
                hideFlags = HideFlags.DontSave
            };
            _xRayCamera = cameraObject.AddComponent<Camera>();
            _xRayCamera.enabled = false;
        }

        private void EnsureRenderTexture()
        {
            int width = Mathf.Max(1, _sourceCamera.pixelWidth);
            int height = Mathf.Max(1, _sourceCamera.pixelHeight);
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
                return;

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            _renderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "XRay Cursor Scanner Preview",
                hideFlags = HideFlags.DontSave
            };
            _renderTexture.Create();
            _scopeImage.texture = _renderTexture;
        }

        private bool IsPointerOverTarget()
        {
            Ray ray = _sourceCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, _sourceCamera.farClipPlane, ~0, QueryTriggerInteraction.Collide))
                return false;

            return hit.transform == _targetRoot.transform || hit.transform.IsChildOf(_targetRoot.transform);
        }

        private void UpdateScopeCrop()
        {
            Vector2 mouse = Input.mousePosition;
            _scopeRect.anchoredPosition = mouse;

            float width = Mathf.Max(1f, Screen.width);
            float height = Mathf.Max(1f, Screen.height);
            float cropWidth = ScopeSize / width;
            float cropHeight = ScopeSize / height;
            float x = Mathf.Clamp01(mouse.x / width) - cropWidth * 0.5f;
            float y = Mathf.Clamp01(mouse.y / height) - cropHeight * 0.5f;
            _scopeImage.uvRect = new Rect(
                Mathf.Clamp(x, 0f, 1f - cropWidth),
                Mathf.Clamp(y, 0f, 1f - cropHeight),
                Mathf.Min(1f, cropWidth),
                Mathf.Min(1f, cropHeight));
        }

        private void SetScopeVisible(bool visible)
        {
            if (_scopeRect != null && _scopeRect.gameObject.activeSelf != visible)
                _scopeRect.gameObject.SetActive(visible);
        }
    }
}
