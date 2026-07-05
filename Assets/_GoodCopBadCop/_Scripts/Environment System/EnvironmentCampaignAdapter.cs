using System;
using VContainer.Unity;

namespace GoodCopBadCop.EnvironmentSystem
{
    public sealed class EnvironmentCampaignAdapter : IInitializable, IDisposable
    {
        private readonly IEnvironmentService service;

        public EnvironmentCampaignAdapter(IEnvironmentService service)
        {
            this.service = service;
        }

        public void Initialize()
        {
            CampaignManager.OnDayChanged += ApplyDay;
            ApplyCurrentDay();
        }

        public void Dispose()
        {
            CampaignManager.OnDayChanged -= ApplyDay;
        }

        private void ApplyCurrentDay()
        {
            if (CampaignManager.Instance != null)
            {
                service.ApplyDay(CampaignManager.Instance.CurrentDay);
                return;
            }

            if (ShiftManager.Instance != null)
            {
                service.ApplyDay(ShiftManager.Instance.CurrentDay);
                return;
            }

            service.ApplyDay(1);
        }

        private void ApplyDay(int day)
        {
            service.ApplyDay(day);
        }
    }

}
