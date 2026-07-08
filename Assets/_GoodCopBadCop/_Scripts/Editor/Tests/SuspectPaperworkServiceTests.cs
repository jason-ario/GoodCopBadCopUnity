using GoodCopBadCop.SuspectPaperwork;
using NUnit.Framework;
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
            Assert.AreEqual("Work", state.EntryReason);
            Assert.AreEqual("31/12/2030", state.ExpirationDate);
            Assert.IsTrue(state.ApplicationVisible);
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
        public void BuildForPreview_MissingDocument_HidesApplication()
        {
            SuspectPaperworkState state = service.BuildForPreview(
                data,
                null,
                new[] { nameof(MissingDocumentAnomaly) },
                1,
                0);

            Assert.IsFalse(state.ApplicationVisible);
        }

        private static void AssertSimilarDigitChanges(string original, string mutated)
        {
            Assert.AreEqual(original.Length, mutated.Length);
            int changedDigits = 0;
            for (int i = 0; i < original.Length; i++)
            {
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
    }
}
