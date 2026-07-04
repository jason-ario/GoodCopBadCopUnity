using System;
using R3;

public sealed class SettingsMenuPresenter : IDisposable
{
    private DisposableBag disposables;

    public SettingsMenuPresenter(ISettingsMenuModel model, ISettingsMenuView view)
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
