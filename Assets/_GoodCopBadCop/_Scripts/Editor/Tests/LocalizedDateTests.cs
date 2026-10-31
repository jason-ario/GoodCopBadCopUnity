using NUnit.Framework;
using System;

namespace GoodCopBadCop.Editor.Tests
{
    /// <summary>Pins <see cref="LocalizedDate"/> output to the publisher's date-format lockit (Date_formats CSV).</summary>
    public sealed class LocalizedDateTests
    {
        private static readonly DateTime Birth = new DateTime(1958, 8, 5);
        private static readonly DateTime Expiry = new DateTime(1989, 12, 1);
        private const LocalizedDate.Style Numeric = LocalizedDate.Style.Numeric;
        private const LocalizedDate.Style Named = LocalizedDate.Style.Named;

        [TestCase("en", "05/08/1958")]
        [TestCase("ru", "05.08.1958")]
        [TestCase("zh-Hans", "1958年8月5日")]
        [TestCase("ja-JP", "1958年8月5日")]
        [TestCase("ko-KR", "1958. 8. 5.")]
        [TestCase("de", "05.08.1958")]
        [TestCase("fr", "05/08/1958")]
        [TestCase("es-ES", "05.08.1958")]
        [TestCase("pt-BR", "5/8/1958")]
        [TestCase("ka", "05.08.1958")]
        [TestCase("pl", "05.08.1958")]
        public void NumericWithYear(string lang, string expected)
            => Assert.AreEqual(expected, LocalizedDate.Format(Birth, lang, Numeric));

        [TestCase("en", "01 DEC")]
        [TestCase("ru", "1 ДЕКАБРЯ")]
        [TestCase("zh-Hans", "12月1日")]
        [TestCase("ja-JP", "12月1日")]
        [TestCase("ko-KR", "12월 1일")]
        [TestCase("de", "1. DEZ.")]
        [TestCase("fr", "1er DÉC.")]
        [TestCase("es-ES", "1 dic.")]
        [TestCase("pt-BR", "1 dez.")]
        [TestCase("ka", "01.12")]
        [TestCase("pl", "1 XII")]
        public void NamedShortUppercaseWithoutYear(string lang, string expected)
            => Assert.AreEqual(expected, LocalizedDate.Format(Expiry, lang, Named, hasYear: false, fullMonth: false, uppercase: true));

        [TestCase("en", "05 August 1958")]
        [TestCase("ru", "5 августа 1958 г.")]
        [TestCase("de", "5. August 1958")]
        [TestCase("fr", "5 août 1958")]
        [TestCase("es-ES", "5 de agosto de 1958")]
        [TestCase("ko-KR", "1958년 8월 5일")]
        [TestCase("pl", "5 VIII 1958")]
        public void NamedFullWithYear(string lang, string expected)
            => Assert.AreEqual(expected, LocalizedDate.Format(Birth, lang, Named, hasYear: true, fullMonth: true));

        [Test]
        public void EnglishNewspaperDateIsUnchanged()
            => Assert.AreEqual("20 Oct 1989", LocalizedDate.Format(new DateTime(1989, 10, 20), "en", Named));

        [TestCase("en", "October")]
        [TestCase("ru", "Октябрь")]
        [TestCase("pl", "Październik")]
        [TestCase("zh-Hans", "十月")]
        [TestCase("ko-KR", "10월")]
        public void StandaloneMonth(string lang, string expected)
            => Assert.AreEqual(expected, LocalizedDate.StandaloneMonthName(10, lang));
    }
}
