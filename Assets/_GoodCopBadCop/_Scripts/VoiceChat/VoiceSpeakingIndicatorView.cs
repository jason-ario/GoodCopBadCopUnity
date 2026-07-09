using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.VoiceChat
{
    public sealed class VoiceSpeakingIndicatorView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image iconImage;
        [SerializeField, Min(0.01f)] private float fadeInSeconds = 0.15f;
        [SerializeField, Min(0.01f)] private float fadeOutSeconds = 0.22f;
        [SerializeField, Min(0f)] private float showDelaySeconds = 0.12f;
        [SerializeField, Min(0f)] private float minimumVisibleSeconds = 0.25f;

        private CancellationTokenSource fadeCancellation;

        public float ShowDelaySeconds => showDelaySeconds;
        public float MinimumVisibleSeconds => minimumVisibleSeconds;

        private void Awake()
        {
            ValidateReferences();
            ConfigureInteraction();
            HideImmediate();
        }

        private void OnDisable()
        {
            CancelFade();
        }

        private void OnDestroy()
        {
            CancelFade();
        }

        public void Show()
        {
            FadeTo(1f, fadeInSeconds).Forget();
        }

        public void Hide()
        {
            FadeTo(0f, fadeOutSeconds).Forget();
        }

        public void HideImmediate()
        {
            if (this == null || canvasGroup == null)
            {
                return;
            }

            CancelFade();
            canvasGroup.alpha = 0f;
        }

        private async UniTaskVoid FadeTo(float targetAlpha, float duration)
        {
            if (this == null || canvasGroup == null)
            {
                return;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            CancelFade();
            CancellationTokenSource currentFadeCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            fadeCancellation = currentFadeCancellation;
            CancellationToken cancellationToken = currentFadeCancellation.Token;

            try
            {
                float startAlpha = canvasGroup.alpha;
                float elapsed = 0f;
                float safeDuration = Mathf.Max(0.01f, duration);

                while (elapsed < safeDuration)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / safeDuration);
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, SmoothStep(t));
                }

                canvasGroup.alpha = targetAlpha;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(fadeCancellation, currentFadeCancellation))
                {
                    fadeCancellation = null;
                    currentFadeCancellation.Dispose();
                }
            }
        }

        private void ValidateReferences()
        {
            if (canvasGroup == null)
            {
                throw new MissingReferenceException($"{nameof(VoiceSpeakingIndicatorView)} requires an assigned {nameof(CanvasGroup)}.");
            }

            if (iconImage == null)
            {
                throw new MissingReferenceException($"{nameof(VoiceSpeakingIndicatorView)} requires an assigned {nameof(Image)}.");
            }
        }

        private void ConfigureInteraction()
        {
            iconImage.raycastTarget = false;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private void CancelFade()
        {
            if (fadeCancellation == null)
            {
                return;
            }

            fadeCancellation.Cancel();
            fadeCancellation.Dispose();
            fadeCancellation = null;
        }

        private static float SmoothStep(float value)
        {
            return value * value * (3f - 2f * value);
        }
    }
}
