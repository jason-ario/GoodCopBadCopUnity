using GoodCopBadCop.SuspectPaperwork;
using NUnit.Framework;
using System;
using System.Globalization;
using UnityEngine;

namespace GoodCopBadCop.Editor.Tests
{
    public sealed class SuspectPaperworkServiceTests
    {
        private SuspectData data;
        private SuspectPaperworkModel model;
        private SuspectPaperworkService service;

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<SuspectData>();
            data.name = "Test Suspect";
            data.FirstName = "Alexei";
            data.LastName = "Sokolov";
            data.DateOfBirth = "12/03/1984";
            data.Sex = "Male";
            data.IDNumber = "1234567";
            data.EntryPermitExpiryDate = "31/12/2030";
            data.IsResident = true;
            data.entryReasons = new SuspectData.EntryReasonSet
            {
                earlyDaysReasons = new[] { "Work" },
                midDaysReasons = new[] { "Family" },
                finalDaysReasons = new[] { "Medical" }
            };
            data.invalidEntryReasons = new SuspectData.EntryReasonSet
            {
                earlyDaysReasons = new[] { "Moonlight" },
                midDaysReasons = new[] { "Fog" },
                finalDaysReasons = new[] { "Static" }
            };

            model = new SuspectPaperworkModel();
            service = new SuspectPaperworkService(model);
        }

        [TearDown]
        public void TearDown()
        {
            model.Dispose();
            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void BuildForPreview_WithoutAnomalies_UsesSourceFields()
        {
            SuspectPaperworkState state = service.BuildForPreview(data, null, System.Array.Empty<string>(), 1, 0);

            Assert.AreEqual("Alexei Sokolov", state.FullName);
            Assert.AreEqual("Alexei Sokolov", state.ApplicationFullName);
            Assert.AreEqual("12/03/1984", state.BirthDate);
            Assert.AreEqual("12/03/1984", state.ApplicationBirthDate);
            Assert.AreEqual("Male", state.Sex);
            Assert.AreEqual("Male", state.ApplicationSex);
            Assert.AreEqual("1234567", state.IdNumber);
            Assert.AreEqual("1234567", state.ApplicationIdNumber);
            Assert.AreEqual("31/12/2030", state.ApplicationExpirationDate);
            Assert.AreEqual("Work", state.EntryReason);
            Assert.AreEqual("31/12/2030", state.ExpirationDate);
            Assert.IsTrue(state.DocumentsVisible);
            Assert.IsTrue(state.ApplicationVisible);
            Assert.IsFalse(state.IsFakeId);
        }

        [Test]
        public void BuildForPreview_BirthDateWrong_IsDeterministicAndDifferent()
        {
            string[] anomalies = { nameof(BirthDateWrong) };

            SuspectPaperworkState first = service.BuildForPreview(data, null, anomalies, 1, 0);
            SuspectPaperworkState second = service.BuildForPreview(data, null, anomalies, 1, 0);

            Assert.AreEqual(first.ApplicationBirthDate, second.ApplicationBirthDate);
            Assert.AreEqual(data.DateOfBirth, first.BirthDate);
            Assert.AreNotEqual(data.DateOfBirth, first.ApplicationBirthDate);
            AssertDocumentDate(data.DateOfBirth, "dd/MM/yyyy");
            AssertDocumentDate(first.ApplicationBirthDate, "dd/MM/yyyy");
        }

        [Test]
        public void BuildForPreview_IdNumberWrong_ChangesOnlyApplicationId()
        {
            string[] anomalies = { nameof(IDNumberWrong) };

            SuspectPaperworkState first = service.BuildForPreview(data, null, anomalies, 2, 3);
            SuspectPaperworkState second = service.BuildForPreview(data, null, anomalies, 2, 3);

            Assert.AreEqual(first.ApplicationIdNumber, second.ApplicationIdNumber);
            Assert.AreEqual(data.IDNumber, first.IdNumber);
            Assert.AreNotEqual(data.IDNumber, first.ApplicationIdNumber);
        }

        [Test]
        public void BuildForPreview_IdNumberWrong_UsesSimilarDigitReplacements()
        {
            string[] anomalies = { nameof(IDNumberWrong) };

            SuspectPaperworkState state = service.BuildForPreview(data, null, anomalies, 2, 3);

            AssertSimilarDigitChanges(data.IDNumber, state.ApplicationIdNumber);
        }

        [Test]
        public void BuildForPreview_FakeId_SetsFakeIdFlagWithoutMutatingApplicationId()
        {
            string[] anomalies = { nameof(FakeIdAnomaly) };

            SuspectPaperworkState state = service.BuildForPreview(data, null, anomalies, 2, 3);

            Assert.IsTrue(state.IsFakeId);
            Assert.AreEqual(data.IDNumber, state.IdNumber);
            Assert.AreEqual(data.IDNumber, state.ApplicationIdNumber);
        }

        [Test]
        public void BuildForPreview_NameWrong_ChangesOnlyApplicationName()
        {
            string[] anomalies = { nameof(NameWrong) };

            SuspectPaperworkState state = service.BuildForPreview(data, null, anomalies, 2, 3);

            Assert.AreEqual("Alexei Sokolov", state.FullName);
            Assert.AreNotEqual("Alexei Sokolov", state.ApplicationFullName);
        }

        [Test]
        public void BuildForPreview_NameWrong_PreservesOriginalLetterCase()
        {
            string[] anomalies = { nameof(NameWrong) };
            data.FirstName = "A";
            data.LastName = string.Empty;

            SuspectPaperworkState state = service.BuildForPreview(data, null, anomalies, 1, 0);

            Assert.AreNotEqual("A", state.ApplicationFullName);
            Assert.IsTrue(char.IsUpper(state.ApplicationFullName[0]));
        }

        [Test]
        public void BuildForPreview_ExpirationDateAnomaly_ChangesOnlyApplicationExpirationDate()
        {
            string[] anomalies = { nameof(ExpirationDateAnomaly) };
            data.EntryPermitExpiryDate = "27 OCT";

            SuspectPaperworkState first = service.BuildForPreview(data, null, anomalies, 2, 3);
            SuspectPaperworkState second = service.BuildForPreview(data, null, anomalies, 2, 3);

            Assert.AreEqual(first.ApplicationExpirationDate, second.ApplicationExpirationDate);
            Assert.AreEqual(data.EntryPermitExpiryDate, first.ExpirationDate);
            Assert.AreNotEqual(data.EntryPermitExpiryDate, first.ApplicationExpirationDate);
            AssertDocumentDate(data.EntryPermitExpiryDate, "dd MMM");
            AssertDocumentDate(first.ApplicationExpirationDate, "dd MMM");
        }

        [Test]
        public void BuildForPreview_ExpirationDateAnomaly_CanChangeTextMonth()
        {
            string[] anomalies = { nameof(ExpirationDateAnomaly) };
            data.EntryPermitExpiryDate = "27 OCT";

            bool changedMonth = false;
            for (int currentDay = 1; currentDay <= 31; currentDay++)
            {
                SuspectPaperworkState state = service.BuildForPreview(data, null, anomalies, currentDay, 0);
                AssertDocumentDate(state.ApplicationExpirationDate, "dd MMM");

                if (!string.Equals(GetMonthToken(data.EntryPermitExpiryDate), GetMonthToken(state.ApplicationExpirationDate), StringComparison.Ordinal))
                {
                    changedMonth = true;
                    break;
                }
            }

            Assert.IsTrue(changedMonth);
        }

        [Test]
        public void BuildForPreview_MissingDocument_HidesAllDocuments()
        {
            SuspectPaperworkState state = service.BuildForPreview(
                data,
                null,
                new[] { nameof(MissingDocumentAnomaly) },
                1,
                0);

            Assert.IsFalse(state.DocumentsVisible);
            Assert.IsFalse(state.ApplicationVisible);
        }

        [Test]
        public void BuildForPreview_MissingDocument_SuppressesOtherDocumentAnomalies()
        {
            SuspectPaperworkState state = service.BuildForPreview(
                data,
                null,
                new[]
                {
                    nameof(MissingDocumentAnomaly),
                    nameof(FakeIdAnomaly),
                    nameof(NameWrong),
                    nameof(BirthDateWrong),
                    nameof(IDNumberWrong),
                    nameof(ExpirationDateAnomaly),
                    nameof(InvalidEntryReason)
                },
                1,
                0);

            Assert.IsFalse(state.DocumentsVisible);
            Assert.IsFalse(state.ApplicationVisible);
            Assert.IsFalse(state.IsFakeId);
            Assert.AreEqual("Alexei Sokolov", state.ApplicationFullName);
            Assert.AreEqual(data.DateOfBirth, state.ApplicationBirthDate);
            Assert.AreEqual(data.IDNumber, state.ApplicationIdNumber);
            Assert.AreEqual(data.EntryPermitExpiryDate, state.ApplicationExpirationDate);
            Assert.AreEqual("Work", state.EntryReason);
        }

        private static void AssertSimilarDigitChanges(string original, string mutated, char preserve = '\0')
        {
            Assert.AreEqual(original.Length, mutated.Length);
            int changedDigits = 0;
            for (int i = 0; i < original.Length; i++)
            {
                if (original[i] == preserve)
                {
                    Assert.AreEqual(original[i], mutated[i]);
                    continue;
                }

                if (original[i] == mutated[i])
                    continue;

                changedDigits++;
                CollectionAssert.Contains(SimilarDigitReplacements(original[i]), mutated[i]);
            }

            Assert.Greater(changedDigits, 0);
        }

        private static char[] SimilarDigitReplacements(char digit)
        {
            switch (digit)
            {
                case '0': return new[] { '8', '9' };
                case '1': return new[] { '7' };
                case '2': return new[] { '3' };
                case '3': return new[] { '5', '8' };
                case '4': return new[] { '7', '9' };
                case '5': return new[] { '3', '6' };
                case '6': return new[] { '5', '8' };
                case '7': return new[] { '1', '4' };
                case '8': return new[] { '0', '3', '6', '9' };
                case '9': return new[] { '0', '8' };
                default: return System.Array.Empty<char>();
            }
        }

        private static void AssertDocumentDate(string value, string format)
        {
            Assert.IsTrue(
                DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                $"Expected '{value}' to parse as '{format}'.");
        }

        private static string GetMonthToken(string value)
        {
            string[] tokens = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return tokens.Length >= 2 ? tokens[1] : string.Empty;
        }
    }
}
