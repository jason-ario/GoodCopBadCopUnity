using System;
using R3;

namespace GoodCopBadCop.UI.SettingsMenu
{
    public interface ISettingsMenuModel
    {
        ReadOnlyReactiveProperty<ESettingsMenuTab> SelectedTab { get; }
    }

    public sealed class SettingsMenuModel : ISettingsMenuModel, IDisposable
    {
        public readonly ReactiveProperty<ESettingsMenuTab> SelectedTabMutable = new(ESettingsMenuTab.Gameplay);

        public ReadOnlyReactiveProperty<ESettingsMenuTab> SelectedTab => SelectedTabMutable;

        public void SelectTab(ESettingsMenuTab tab)
        {
            if (SelectedTabMutable.Value == tab)
            {
                SelectedTabMutable.OnNext(tab);
                return;
            }

            SelectedTabMutable.Value = tab;
        }

        public void Dispose()
        {
            SelectedTabMutable.Dispose();
        }
    }

}
