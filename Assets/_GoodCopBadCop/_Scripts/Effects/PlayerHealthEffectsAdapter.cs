using System;
using GoodCopBadCop.Player;
using R3;
using VContainer.Unity;

namespace GoodCopBadCop.Effects
{
    public sealed class PlayerHealthEffectsAdapter : IInitializable, IDisposable
    {
        private readonly IPlayerRuntimeModel playerRuntimeModel;
        private readonly IEffectService effectService;
        private DisposableBag disposables;
        private global::PlayerHealth playerHealth;
        private float previousHealth;

        public PlayerHealthEffectsAdapter(
            IPlayerRuntimeModel playerRuntimeModel,
            IEffectService effectService)
        {
            this.playerRuntimeModel = playerRuntimeModel;
            this.effectService = effectService;
        }

        public void Initialize()
        {
            playerRuntimeModel.LocalPlayer.Subscribe(_ => AttachToLocalPlayer()).AddTo(ref disposables);
            AttachToLocalPlayer();
        }

        public void Dispose()
        {
            DetachFromHealth();
            disposables.Dispose();
        }

        private void AttachToLocalPlayer()
        {
            DetachFromHealth();

            global::PlayerInstance player = playerRuntimeModel.LocalPlayer.CurrentValue;
            if (player == null)
                return;

            playerHealth = player.PlayerHealth != null ? player.PlayerHealth : player.GetComponent<global::PlayerHealth>();
            if (playerHealth == null)
                return;

            previousHealth = playerHealth.Health;
            playerHealth.OnHealthChanged += HandleHealthChanged;
            playerHealth.OnDeath += HandleDeath;
            playerHealth.OnRespawn += HandleRespawn;
        }

        private void DetachFromHealth()
        {
            if (playerHealth == null)
                return;

            playerHealth.OnHealthChanged -= HandleHealthChanged;
            playerHealth.OnDeath -= HandleDeath;
            playerHealth.OnRespawn -= HandleRespawn;
            playerHealth = null;
        }

        private void HandleHealthChanged()
        {
            if (playerHealth == null)
                return;

            float currentHealth = playerHealth.Health;
            if (currentHealth < previousHealth && currentHealth > 0f)
            {
                string effectKey = string.IsNullOrWhiteSpace(playerHealth.LastHealthEffectKey)
                    ? EffectKeys.DefaultPlayerDamage
                    : playerHealth.LastHealthEffectKey;
                effectService.PlayByKey(effectKey);
            }
            else if (currentHealth > previousHealth)
            {
                string effectKey = string.IsNullOrWhiteSpace(playerHealth.LastHealthEffectKey)
                    ? EffectKeys.PlayerHeal
                    : playerHealth.LastHealthEffectKey;
                effectService.PlayByKey(effectKey);
            }

            previousHealth = currentHealth;
        }

        private void HandleDeath()
        {
            effectService.PlayByKey(EffectKeys.PlayerDeath);
            if (playerHealth != null)
                previousHealth = playerHealth.Health;
        }

        private void HandleRespawn()
        {
            if (playerHealth != null)
                previousHealth = playerHealth.Health;
        }
    }
}
