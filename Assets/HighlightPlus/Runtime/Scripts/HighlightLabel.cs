using System;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace HighlightPlus {

    [ExecuteAlways]
    public partial class HighlightLabel : MonoBehaviour {

        public Camera cam;

        [NonSerialized]
        public Transform target;
        [NonSerialized]
        public Vector3 localPosition;
        [NonSerialized]
        public Vector3 worldOffset;

        [NonSerialized]
        public Vector2 viewportOffset;

        [NonSerialized]
        public bool isVisible;

        [NonSerialized]
        public LabelAlignment labelAlignment = LabelAlignment.Auto;

        [NonSerialized]
        public bool labelRelativeAlignment;
        [NonSerialized]
        public Transform labelAlignmentTransform;

        public GameObject labelPrefab;

        internal bool isPooled;

        [NonSerialized]
        public bool isWorldSpace;

        [NonSerialized]
        public float worldSpaceScale = 0.01f;

        static Material uiAlwaysOnTopMaterial;
        static int zTestModeID;

        Canvas canvas;
        Image line;
        TextMeshProUGUI text;
        RectTransform panel;
        CanvasGroup canvasGroup;

        [NonSerialized]
        public float labelMaxDistance = 250f;
        [NonSerialized]
        public float labelFadeStartDistance = 200f;
        [NonSerialized]
        public bool labelScaleByDistance;
        [NonSerialized]
        public float labelScaleMin = 1f;
        [NonSerialized]
        public float labelScaleMax = 1f;

        [NonSerialized]
        public float highlightAlpha = 1f;

        public virtual float alpha {
            get {
                return highlightAlpha;
            }
            set {
                highlightAlpha = value;
            }
        }

        public virtual Color textColor {
            get {
                return text?.color ?? Color.white;
            }
            set {
                if (text != null) {
                    text.color = value;
                }
            }
        }

        public virtual string textLabel {
            get {
                return text?.text ?? "";
            }
            set {
                if (text != null) {
                    text.text = value;
                }
            }
        }

        public virtual float textSize {
            get {
                return text?.fontSize ?? 14;
            }
            set {
                if (text != null) {
                    text.fontSize = value;
                }
            }
        }


        public virtual float width {
            get {
                return text?.rectTransform.sizeDelta.x ?? 200;
            }
            set {
                if (text == null) return;
                if (text.rectTransform != null) {
                    Vector2 currentSize = text.rectTransform.sizeDelta;
                    if (currentSize.x != value) {
                        text.rectTransform.sizeDelta = new Vector2(value, currentSize.y);
                    }
                }
                RectTransform panelRectTransform = panel.GetComponent<RectTransform>();
                if (panelRectTransform != null) {
                    Vector2 currentSize = panelRectTransform.sizeDelta;
                    if (currentSize.x != value) {
                        panelRectTransform.sizeDelta = new Vector2(value, currentSize.y);
                    }
                }
            }
        }

        void Awake () {
            canvas = GetComponent<Canvas>();
            line = GetComponentInChildren<Image>();
            text = GetComponentInChildren<TextMeshProUGUI>();
            panel = transform.GetChild(0).GetComponentInChildren<RectTransform>();
            canvasGroup = GetComponent<CanvasGroup>();
            if (zTestModeID == 0) {
                zTestModeID = Shader.PropertyToID("_ZTestMode");
            }
        }

        /// <summary>
        /// Configures the label Canvas render mode for screen space or world space rendering.
        /// </summary>
        public void ConfigureCanvas(bool worldSpace) {
            if (isWorldSpace == worldSpace) return;
            isWorldSpace = worldSpace;
            if (canvas == null) return;
            if (worldSpace) {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = cam;
                SetAlwaysOnTop(true);
            } else {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                SetAlwaysOnTop(false);
            }
        }

        void SetAlwaysOnTop(bool enabled) {
            // UI Image (the connecting line)
            if (line != null) {
                if (enabled) {
                    if (uiAlwaysOnTopMaterial == null) {
                        uiAlwaysOnTopMaterial = new Material(Shader.Find("UI/Default"));
                        uiAlwaysOnTopMaterial.SetFloat(ShaderParams.ZTest, (float)CompareFunction.Always);
                    }
                    line.material = uiAlwaysOnTopMaterial;
                } else {
                    line.material = null;
                }
            }

            // TextMeshPro text
            if (text != null) {
                text.fontMaterial.SetFloat(zTestModeID, (float)(enabled ? CompareFunction.Always : CompareFunction.LessEqual));
            }
        }

        /// <summary>
        /// Return the label to the pool
        /// </summary>
        public virtual void ReturnToPool () {
            if (!isPooled) return;
            gameObject.SetActive(false);
            HighlightLabelPoolManager.ReturnToPool(this);
        }


        public virtual void SetPosition(Transform target, Vector3 localPosition, Vector3 worldOffset, Vector2 viewportOffset = default) {
            this.target = target;
            this.localPosition = localPosition;
            this.worldOffset = worldOffset;
            this.viewportOffset = viewportOffset;
        }

        public virtual void UpdatePosition () {
            if (panel == null || text == null) return;
            if (cam == null) {
                cam = Camera.main;
                if (cam == null) return;
            }
            if (target == null) return;

            Vector3 worldPosition = target.TransformPoint(localPosition);
            Vector3 worldWithOffset = worldPosition + worldOffset;

            // Distance-based visibility & scaling (common to both modes)
            float distanceToCam = Vector3.Distance(cam.transform.position, worldWithOffset);
            float visibleAlpha = 1f;
            if (labelMaxDistance > 0f && distanceToCam > labelMaxDistance) {
                if (canvasGroup != null) canvasGroup.alpha = 0f;
                return;
            }
            if (labelFadeStartDistance > 0f && labelMaxDistance > labelFadeStartDistance && distanceToCam > labelFadeStartDistance) {
                float t = (distanceToCam - labelFadeStartDistance) / (labelMaxDistance - labelFadeStartDistance);
                visibleAlpha = Mathf.Clamp01(1f - t);
            }
            if (canvasGroup != null) {
                canvasGroup.alpha = highlightAlpha * visibleAlpha;
            }

            if (isWorldSpace) {
                UpdatePositionWorldSpace(worldWithOffset, distanceToCam);
            } else {
                UpdatePositionScreenSpace(worldWithOffset, distanceToCam);
            }
        }

        void UpdatePositionWorldSpace(Vector3 worldWithOffset, float distanceToCam) {
            // Position canvas in world space
            transform.position = worldWithOffset;

            // Billboard: face camera
            transform.rotation = cam.transform.rotation;

            // Scale: base worldSpaceScale + optional distance scaling
            float scale = worldSpaceScale;
            if (labelScaleByDistance && labelMaxDistance > 0f) {
                float tScale = Mathf.Clamp01(distanceToCam / labelMaxDistance);
                scale *= Mathf.Lerp(labelScaleMax, labelScaleMin, tScale);
            }
            transform.localScale = new Vector3(scale, scale, scale);

            // Panel keeps identity transform in world space mode
            panel.localScale = Vector3.one;
            panel.localPosition = Vector3.zero;
        }

        void UpdatePositionScreenSpace(Vector3 worldWithOffset, float distanceToCam) {
            // If anchor point is outside of view, hide label without changing active state
            Vector3 vp = cam.WorldToViewportPoint(worldWithOffset);
            bool onScreen = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            if (!onScreen) {
                if (canvasGroup != null) {
                    canvasGroup.alpha = 0f;
                }
                return;
            }

            if (labelScaleByDistance && labelMaxDistance > 0f) {
                float tScale = Mathf.Clamp01(distanceToCam / labelMaxDistance);
                float scale = Mathf.Lerp(labelScaleMax, labelScaleMin, tScale);
                panel.localScale = new Vector3(scale, scale, 1f);
            } else {
                panel.localScale = Vector3.one;
            }

            Vector3 screenPosition = cam.WorldToScreenPoint(worldWithOffset);
            // Apply viewport offset in screen space (normalized -1 to 1 range)
            screenPosition.x += viewportOffset.x * Screen.width;
            screenPosition.y += viewportOffset.y * Screen.height;
            panel.position = screenPosition;

            bool flip = false;
            if (labelRelativeAlignment && labelAlignmentTransform != null) {
                Vector3 dirToCam = (cam.transform.position - labelAlignmentTransform.position).normalized;
                float dot = Vector3.Dot(labelAlignmentTransform.forward, dirToCam);
                flip = dot > 0;
            }

            if (labelAlignment == LabelAlignment.Left) {
                panel.pivot = flip ? Vector2.zero : Vector2.right;
                text.alignment = flip ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.BottomLeft;
            } else if (labelAlignment == LabelAlignment.Right) {
                panel.pivot = flip ? Vector2.right : Vector2.zero;
                text.alignment = flip ? TextAlignmentOptions.BottomLeft : TextAlignmentOptions.BottomRight;
            } else if (labelAlignment == LabelAlignment.Auto && panel.position.x + width * 2f > cam.pixelWidth * 0.95f) {
                panel.pivot = Vector2.right;
                text.alignment = TextAlignmentOptions.BottomLeft;
            } else {
                panel.pivot = Vector2.zero;
                text.alignment = TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>
        /// Show the label
        /// </summary>
        public virtual void Show () {
            isVisible = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) {
                HighlightLabelPoolManager.Refresh();
            }
#endif
        }

        /// <summary>
        /// Hide the label
        /// </summary>
        public virtual void Hide () {
            if (this == null) return;
            gameObject.SetActive(false);
            isVisible = false;
        }

        /// <summary>
        /// Hide & destroy the label
        /// </summary>
        public virtual void Release () {
            if (this == null) return;
            Hide();
            if (isPooled) {
                ReturnToPool();
            }
        }
    }
}
