using System;
using R3;
using VContainer.Unity;

public sealed class SettingsMenuPresenter : IInitializable, IDisposable
{
    private readonly ISettingsMenuModel model;
    private readonly ISettingsMenuView view;
    private DisposableBag disposables;

    public SettingsMenuPresenter(
        ISettingsMenuModel model,
        ISettingsMenuView view)
    {
        this.model = model;
        this.view = view;
    }

    public void Initialize()
    {
        model.SelectedTab
            .Subscribe(view.ShowTab)
            .AddTo(ref disposables);
    }

    public void Dispose()
    {
        disposables.Dispose();
    }
}
