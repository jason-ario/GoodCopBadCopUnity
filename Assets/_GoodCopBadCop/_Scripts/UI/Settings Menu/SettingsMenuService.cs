using System.Collections.Generic;

namespace GoodCopBadCop.UI.SettingsMenu
{
    public interface ISettingsMenuService
    {
        void SelectTab(ESettingsMenuTab tab);
        void SelectDefaultTab(IReadOnlyList<ESettingsMenuTab> availableTabs);
    }

    public sealed class SettingsMenuService : ISettingsMenuService
    {
        private readonly SettingsMenuModel model;

        public SettingsMenuService(SettingsMenuModel model)
        {
            this.model = model;
        }

        public void SelectTab(ESettingsMenuTab tab)
        {
            model.SelectTab(tab);
        }

        public void SelectDefaultTab(IReadOnlyList<ESettingsMenuTab> availableTabs)
        {
            if (availableTabs == null || availableTabs.Count == 0)
            {
                SelectTab(ESettingsMenuTab.Graphics);
                return;
            }

            SelectTab(availableTabs[0]);
        }
    }

}
