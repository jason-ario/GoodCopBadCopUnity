using System;
using GoodCopBadCop.Player;
using R3;
using UnityEngine;
using VContainer.Unity;

namespace GoodCopBadCop.Effects
{
    /// <summary>
    /// Drives the drunk fullscreen effect while <see cref="PlayerDrunkState.IsDrunk"/> is true.
    /// Fires <see cref="EffectKeys.PlayerDrunk"/> immediately on drunk start, then repeats
    /// every <see cref="EffectRepeatInterval"/> seconds until the state clears. Also fades the
    /// sustained <see cref="DrunkScreenEffectView"/> look (vignette + woozy lens) in and out.
    /// </summary>
    public sealed class PlayerDrunkEffectsAdapter : IInitializable, IDisposable, ITickable
    {
        private const float EffectRepeatInterval = 2.5f;

        private readonly IPlayerRuntimeModel playerRuntimeModel;
        private readonly IEffectService effectService;

        private DisposableBag disposables;
        private PlayerDrunkState playerDrunkState;
        private float nextEffectTime = float.MaxValue;
        private DrunkScreenEffectView screenEffectView;

        public PlayerDrunkEffectsAdapter(
            IPlayerRuntimeModel playerRuntimeModel,
            IEffectService effectService)
        {
            this.playerRuntimeModel = playerRuntimeModel;
            this.effectService = effectService;
        }

        public void Initialize()
        {
            playerRuntimeModel.LocalPlayer
                .Subscribe(_ => AttachToLocalPlayer())
                .AddTo(ref disposables);

            AttachToLocalPlayer();
        }

        public void Dispose()
        {
            DetachFromDrunkState();
            disposables.Dispose();

            if (screenEffectView != null)
                UnityEngine.Object.Destroy(screenEffectView.gameObject);
            screenEffectView = null;
        }

        public void Tick()
        {
            if (playerDrunkState == null || !playerDrunkState.IsDrunk)
                return;

            if (Time.unscaledTime >= nextEffectTime)
            {
                nextEffectTime = Time.unscaledTime + EffectRepeatInterval;
                effectService.PlayByKey(EffectKeys.PlayerDrunk);
            }
        }

        private void AttachToLocalPlayer()
        {
            DetachFromDrunkState();

            PlayerInstance player = playerRuntimeModel.LocalPlayer.CurrentValue;
            if (player == null)
                return;

            playerDrunkState = player.PlayerDrunkState;
            if (playerDrunkState == null)
                return;

            playerDrunkState.OnDrunkChanged += HandleDrunkChanged;

            if (playerDrunkState.IsDrunk)
                ScheduleImmediately();
        }

        private void DetachFromDrunkState()
        {
            if (playerDrunkState == null)
                return;

            playerDrunkState.OnDrunkChanged -= HandleDrunkChanged;
            playerDrunkState = null;
            nextEffectTime = float.MaxValue;
            SetScreenEffect(false);
        }

        private void HandleDrunkChanged(bool isDrunk)
        {
            if (isDrunk)
                ScheduleImmediately();
            else
            {
                nextEffectTime = float.MaxValue;
                SetScreenEffect(false);
            }
        }

        /// <summary>Schedules the effect to fire on the very next <see cref="Tick"/>.</summary>
        private void ScheduleImmediately()
        {
            nextEffectTime = 0f;
            SetScreenEffect(true);
        }

        /// <summary>Fades the sustained drunk look; the view is created the first time it is needed.</summary>
        private void SetScreenEffect(bool isDrunk)
        {
            if (screenEffectView == null)
            {
                if (!isDrunk) return;
                screenEffectView = DrunkScreenEffectView.CreateDefaultView();
            }

            screenEffectView.SetDrunk(isDrunk);
        }
    }
}
