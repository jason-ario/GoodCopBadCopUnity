using NUnit.Framework;
using UnityEngine;

namespace GoodCopBadCop.Editor.Tests
{
    public sealed class MutantLineupSetTests
    {
        private MutantLineupSet lineup;
        private GameObject alexeiObject;
        private GameObject alternateObject;
        private MutantSuspectBehaviour alexei;
        private MutantSuspectBehaviour alternate;

        [SetUp]
        public void SetUp()
        {
            lineup = ScriptableObject.CreateInstance<MutantLineupSet>();
            alexeiObject = new GameObject("Alexei");
            alternateObject = new GameObject("Alternate Mutant");
            alexei = alexeiObject.AddComponent<MutantSuspectBehaviour>();
            alternate = alternateObject.AddComponent<MutantSuspectBehaviour>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(lineup);
            Object.DestroyImmediate(alexeiObject);
            Object.DestroyImmediate(alternateObject);
        }

        [Test]
        public void GetRandomExcluding_AlternativeExists_NeverReturnsPreviousMutant()
        {
            lineup.mutants.Add(alexei);
            lineup.mutants.Add(alternate);

            for (int i = 0; i < 10; i++)
                Assert.AreSame(alternate, lineup.GetRandomExcluding(alexei));
        }

        [Test]
        public void GetRandomExcluding_PreviousMutantIsOnlyEntry_ReturnsPreviousMutant()
        {
            lineup.mutants.Add(alexei);

            Assert.AreSame(alexei, lineup.GetRandomExcluding(alexei));
        }
    }
}
