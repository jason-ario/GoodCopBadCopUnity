using VContainer;
using VContainer.Unity;

public sealed class MainSceneLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<SettingsMenuView>().As<ISettingsMenuView>();
        builder.Register<SettingsMenuModel>(Lifetime.Scoped).AsSelf().As<ISettingsMenuModel>();
        builder.Register<ISettingsMenuService, SettingsMenuService>(Lifetime.Scoped);
        builder.RegisterEntryPoint<SettingsMenuPresenter>(Lifetime.Scoped);
    }
}
