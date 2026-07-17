using System;
using GoodCopBadCop.CameraSystem;
using GoodCopBadCop.UI.SettingsMenu;
using GoodCopBadCop.EnvironmentSystem;
using GoodCopBadCop.Effects;
using GoodCopBadCop.Audio;
using GoodCopBadCop.Player;
using GoodCopBadCop.Population;
using GoodCopBadCop.RoomSystem;
using GoodCopBadCop.Settings;
using GoodCopBadCop.SuspectPaperwork;
using GoodCopBadCop.VoiceChat;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.Infrastructure
{
    public sealed class MainSceneLifetimeScope : LifetimeScope
    {
        [SerializeField] private EnvironmentSchedule environmentSchedule;
        [SerializeField] private EffectCatalog effectCatalog;
        [SerializeField] private PopulationConfig populationConfig;

        protected override void Configure(IContainerBuilder builder)
        {
            if (effectCatalog == null)
            {
                throw new InvalidOperationException("Effect catalog is not assigned in MainSceneLifetimeScope.");
            }

            if (populationConfig == null)
            {
                throw new InvalidOperationException("Population config is not assigned in MainSceneLifetimeScope.");
            }

            builder.Register<SettingsModel>(Lifetime.Scoped).AsSelf().As<ISettingsModel>();
            builder.Register<ISettingsService, SettingsService>(Lifetime.Scoped);
            builder.Register<ISettingsScreenAdapter, UnitySettingsScreenAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<SettingsApplier>(Lifetime.Scoped);
            builder.Register<ILegacyGameObjectInjector, LegacyGameObjectInjector>(Lifetime.Scoped);
            builder.Register<ICameraService, CameraService>(Lifetime.Scoped);
            builder.Register<IAudioService, AudioService>(Lifetime.Scoped);
            builder.Register<IRoomService, RoomService>(Lifetime.Scoped);
            builder.Register<SuspectPaperworkModel>(Lifetime.Scoped).AsSelf().As<ISuspectPaperworkModel>();
            builder.Register<ISuspectPaperworkService, SuspectPaperworkService>(Lifetime.Scoped);
            builder.RegisterInstance(populationConfig);
            builder.Register<PopulationModel>(Lifetime.Scoped).AsSelf().As<IPopulationModel>();
            builder.Register<IPopulationService, PopulationService>(Lifetime.Scoped);
            builder.RegisterInstance(effectCatalog).As<IEffectCatalog>();
            builder.Register<IFullscreenEffectService, FullscreenEffectService>(Lifetime.Scoped);
            builder.Register<IEffectService, EffectService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<PlayerHealthEffectsAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<PlayerDrunkEffectsAdapter>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<global::SuspectController>();
            builder.RegisterComponentInHierarchy<global::Thermometer>();

            builder.Register<PlayerRuntimeModel>(Lifetime.Scoped).AsSelf().As<IPlayerRuntimeModel>();
            builder.Register<IPlayerRuntimeService, PlayerRuntimeService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<PlayerRuntimeAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<PlayerControlsSettingsAdapter>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<global::CampaignManager>();
            builder.RegisterComponentInHierarchy<global::PC>();
            builder.RegisterComponentInHierarchy<global::ShiftManager>();

            builder.Register<VoiceChatModel>(Lifetime.Scoped).AsSelf().As<IVoiceChatModel>();
            builder.RegisterEntryPoint<VoiceChatCommsRuntime>(Lifetime.Scoped);
            builder.Register<IVoiceChatService, VoiceChatService>(Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<VoiceSpeakingIndicatorView>();
            builder.RegisterEntryPoint<VoiceChatSettingsAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<DissonanceVoiceChatAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<VoiceSpeakingIndicatorPresenter>(Lifetime.Scoped);

            // Environment system registered before SettingsMenuPresenter so that
            // EnvironmentRenderAdapter and EnvironmentCampaignAdapter are earlier in
            // the IInitializable collection. CollectionInstanceProvider builds the list
            // eagerly with no per-element try-catch, so any registration that fails to
            // resolve (e.g. SettingsMenuPresenter when SettingsMenuView is absent from
            // the scene) aborts the build for every entry point that follows it.
            // Keeping environment entry points here ensures the sky/fog updates even
            // when the settings menu is unavailable.
            builder.RegisterInstance(environmentSchedule);
            builder.RegisterComponentInHierarchy<VolumetricFog>();
            builder.Register<EnvironmentModel>(Lifetime.Scoped).AsSelf().As<IEnvironmentModel>();
            builder.Register<IEnvironmentService, EnvironmentService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<EnvironmentRenderAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<EnvironmentCampaignAdapter>(Lifetime.Scoped);

            builder.RegisterComponentInHierarchy<SettingsRedesignPreviewController>().As<ISettingsMenuView>();
            builder.RegisterComponentInHierarchy<MainMenuController>();
            builder.RegisterComponentInHierarchy<PauseMenuController>();
            builder.Register<SettingsMenuModel>(Lifetime.Scoped).AsSelf().As<ISettingsMenuModel>();
            builder.Register<ISettingsMenuService, SettingsMenuService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<SettingsMenuPresenter>(Lifetime.Scoped);
        }
    }
}
