using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.Effects
{
    public sealed class FullscreenEffectView : MonoBehaviour
    {
        [SerializeField] private Image overlayImage;

        private Coroutine activeRoutine;

        public static FullscreenEffectView CreateDefaultView()
        {
            var canvasObject = new GameObject("Fullscreen Effect View");
            Object.DontDestroyOnLoad(canvasObject);

            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            var imageObject = new GameObject("Overlay");
            imageObject.transform.SetParent(canvasObject.transform, false);

            var image = imageObject.AddComponent<Image>();
            image.enabled = false;
            image.raycastTarget = false;

            RectTransform rectTransform = image.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            var view = canvasObject.AddComponent<FullscreenEffectView>();
            view.overlayImage = image;
            return view;
        }

        public void Play(FullscreenEffectSettings settings)
        {
            if (overlayImage == null)
            {
                Debug.LogWarning("[FullscreenEffectView] Overlay image is not assigned.", this);
                return;
            }

            if (settings.OverlaySprite == null)
            {
                Debug.LogWarning("[FullscreenEffectView] Overlay sprite is not assigned.", this);
                return;
            }

            if (activeRoutine != null)
                StopCoroutine(activeRoutine);

            activeRoutine = StartCoroutine(PlayRoutine(settings));
        }

        private IEnumerator PlayRoutine(FullscreenEffectSettings settings)
        {
            overlayImage.sprite = settings.OverlaySprite;
            overlayImage.enabled = true;

            float duration = Mathf.Max(0.01f, settings.Duration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float alpha = settings.Opacity * settings.OpacityCurve.Evaluate(t);
                Color color = settings.Tint;
                color.a *= alpha;
                overlayImage.color = color;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            overlayImage.enabled = false;
            activeRoutine = null;
        }
    }
}