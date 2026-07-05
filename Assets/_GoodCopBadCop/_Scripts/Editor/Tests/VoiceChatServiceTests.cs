using GoodCopBadCop.VoiceChat;
using NUnit.Framework;

namespace GoodCopBadCop.Tests.Editor.VoiceChat
{
    public sealed class VoiceChatServiceTests
    {
        [Test]
        public void SetEnabled_UpdatesModel()
        {
            using var model = new VoiceChatModel();
            var service = new VoiceChatService(model);

            service.SetEnabled(false);

            Assert.That(model.IsEnabled.CurrentValue, Is.False);
        }

        [Test]
        public void SetProximityRange_ClampsToDissonanceSupportedRange()
        {
            using var model = new VoiceChatModel();
            var service = new VoiceChatService(model);

            service.SetProximityRange(-10);
            Assert.That(model.ProximityRange.CurrentValue, Is.EqualTo(VoiceChatService.MinimumProximityRange));

            service.SetProximityRange(1000);
            Assert.That(model.ProximityRange.CurrentValue, Is.EqualTo(VoiceChatService.MaximumProximityRange));
        }

        [Test]
        public void SetMicrophoneName_NullValue_BecomesEmptyString()
        {
            using var model = new VoiceChatModel();
            var service = new VoiceChatService(model);

            service.SetMicrophoneName(null);

            Assert.That(model.MicrophoneName.CurrentValue, Is.Empty);
        }
    }
}
