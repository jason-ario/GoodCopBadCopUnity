using System;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.VoiceChat
{
    public sealed class VoiceSpeakingIndicatorPresenter : IInitializable, ITickable, IDisposable
    {
        private readonly IVoiceChatModel model;
        private readonly VoiceSpeakingIndicatorView view;
        private DisposableBag disposables;
        private bool desiredVisible;
        private bool isVisible;
        private float showDelayRemaining;
        private float visibleTimeRemaining;

        public VoiceSpeakingIndicatorPresenter(
            IVoiceChatModel model,
            VoiceSpeakingIndicatorView view)
        {
            this.model = model;
            this.view = view;
        }

        public void Initialize()
        {
            view.HideImmediate();
            model.IsLocalSpeaking.Subscribe(_ => RefreshDesiredState()).AddTo(ref disposables);
            model.IsEnabled.Subscribe(_ => RefreshDesiredState()).AddTo(ref disposables);
            model.IsMuted.Subscribe(_ => RefreshDesiredState()).AddTo(ref disposables);
            model.IsDeafened.Subscribe(_ => RefreshDesiredState()).AddTo(ref disposables);
            model.IsCommsAvailable.Subscribe(_ => RefreshDesiredState()).AddTo(ref disposables);
            RefreshDesiredState();
        }

        public void Tick()
        {
            float deltaTime = UnityEngine.Time.unscaledDeltaTime;

            if (showDelayRemaining > 0f)
            {
                showDelayRemaining -= deltaTime;
                if (showDelayRemaining <= 0f && desiredVisible)
                {
                    Show();
                }
            }

            if (isVisible && visibleTimeRemaining > 0f)
            {
                visibleTimeRemaining -= deltaTime;
                if (visibleTimeRemaining <= 0f && !desiredVisible)
                {
                    Hide();
                }
            }
        }

        public void Dispose()
        {
            disposables.Dispose();

            if (view != null)
            {
                view.HideImmediate();
            }
        }

        private void RefreshDesiredState()
        {
            bool canShow = model.IsEnabled.CurrentValue
                && !model.IsMuted.CurrentValue
                && !model.IsDeafened.CurrentValue
                && model.IsCommsAvailable.CurrentValue;
            bool shouldShow = canShow && model.IsLocalSpeaking.CurrentValue;

            if (desiredVisible == shouldShow)
            {
                return;
            }

            desiredVisible = shouldShow;

            if (desiredVisible)
            {
                showDelayRemaining = view.ShowDelaySeconds;
                return;
            }

            showDelayRemaining = 0f;

            if (isVisible && (!canShow || visibleTimeRemaining <= 0f))
            {
                Hide();
            }
        }

        private void Show()
        {
            isVisible = true;
            visibleTimeRemaining = view.MinimumVisibleSeconds;
            view.Show();
        }

        private void Hide()
        {
            isVisible = false;
            visibleTimeRemaining = 0f;
            view.Hide();
        }
    }
}
