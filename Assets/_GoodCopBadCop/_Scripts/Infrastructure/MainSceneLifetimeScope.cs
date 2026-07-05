using GoodCopBadCop.UI.SettingsMenu;
using GoodCopBadCop.EnvironmentSystem;
using GoodCopBadCop.Settings;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using VolumetricFogAndMist2;

namespace GoodCopBadCop.Infrastructure
{
    public sealed class MainSceneLifetimeScope : LifetimeScope
    {
        [SerializeField] private EnvironmentSchedule environmentSchedule;

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<SettingsModel>(Lifetime.Scoped).AsSelf().As<ISettingsModel>();
            builder.Register<ISettingsService, SettingsService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<SettingsApplier>(Lifetime.Scoped);

            builder.RegisterComponentInHierarchy<SettingsMenuView>().As<ISettingsMenuView>();
            builder.Register<SettingsMenuModel>(Lifetime.Scoped).AsSelf().As<ISettingsMenuModel>();
            builder.Register<ISettingsMenuService, SettingsMenuService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<SettingsMenuPresenter>(Lifetime.Scoped);

            builder.RegisterInstance(environmentSchedule);
            builder.RegisterComponentInHierarchy<VolumetricFog>();
            builder.Register<EnvironmentModel>(Lifetime.Scoped).AsSelf().As<IEnvironmentModel>();
            builder.Register<IEnvironmentService, EnvironmentService>(Lifetime.Scoped);
            builder.RegisterEntryPoint<EnvironmentRenderAdapter>(Lifetime.Scoped);
            builder.RegisterEntryPoint<EnvironmentCampaignAdapter>(Lifetime.Scoped);
        }
    }

}
