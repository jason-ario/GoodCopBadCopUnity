using GoodCopBadCop.Population;
using NUnit.Framework;
using UnityEngine;

namespace GoodCopBadCop.Editor.Tests
{
    public sealed class PopulationServiceTests
    {
        private PopulationConfig config;
        private PopulationModel model;
        private PopulationService service;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<PopulationConfig>();
            config.totalPopulation = 100;
            config.backgroundDeathsPerMutantPerDay = new Vector2Int(1, 1);
            model = new PopulationModel();
            service = new PopulationService(model, config);
        }

        [TearDown]
        public void TearDown()
        {
            model.Dispose();
            UnityEngine.Object.DestroyImmediate(config);
        }

        [Test]
        public void Initialize_UsesContactableCountAndBackgroundRemainder()
        {
            service.Initialize(null, 30);

            Assert.AreEqual(100, model.TotalPopulation.CurrentValue);
            Assert.AreEqual(100, model.PopulationAlive.CurrentValue);
            Assert.AreEqual(30, model.ContactableAlive.CurrentValue);
            Assert.AreEqual(70, model.BackgroundAlive.CurrentValue);
        }

        [Test]
        public void RecordPassed_ScoreAboveEighty_KillsBackgroundResidentOnce()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 81);

            service.RecordContactableResidentPassed(record, activeAnomalyCount: 0);
            service.SimulateDay(2, new[] { record });
            service.SimulateDay(3, new[] { record });

            Assert.AreEqual(69, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(1, model.BackgroundDead.CurrentValue);
            Assert.AreEqual(0, model.DeadOvernight.CurrentValue);
            Assert.AreEqual(99, model.PopulationAlive.CurrentValue);
            Assert.IsFalse(record.populationKillPending);
        }

        [Test]
        public void RecordPassed_ScoreAtEighty_DoesNotKill()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 80);

            service.RecordContactableResidentPassed(record, activeAnomalyCount: 0);
            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(70, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(0, model.DeadOvernight.CurrentValue);
            Assert.IsFalse(record.populationKillPending);
        }

        [Test]
        public void RecordPassed_MoreThanTenAnomalies_KillsBackgroundResident()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 0);

            service.RecordContactableResidentPassed(record, activeAnomalyCount: 11);
            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(69, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(1, model.DeadOvernight.CurrentValue);
        }

        [Test]
        public void RecordPassed_TenAnomalies_DoesNotKill()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 0);

            service.RecordContactableResidentPassed(record, activeAnomalyCount: 10);
            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(70, model.BackgroundAlive.CurrentValue);
            Assert.IsFalse(record.populationKillPending);
        }

        [Test]
        public void SimulateDay_DeathsClampAtRemainingBackgroundPopulation()
        {
            config.totalPopulation = 2;
            config.backgroundDeathsPerMutantPerDay = new Vector2Int(4, 4);
            service.Initialize(null, 1);
            SuspectRecord first = CreateRecord(infectionScore: 81, populationKillPending: true);
            SuspectRecord second = CreateRecord(infectionScore: 81, populationKillPending: true);

            service.SimulateDay(2, new[] { first, second });

            Assert.AreEqual(0, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(1, model.BackgroundDead.CurrentValue);
            Assert.AreEqual(1, model.DeadOvernight.CurrentValue);
            Assert.AreEqual(1, model.PopulationAlive.CurrentValue);
        }

        [Test]
        public void SimulateDay_NoBackgroundResidentsStillConsumesPendingKill()
        {
            config.totalPopulation = 1;
            service.Initialize(null, 1);
            SuspectRecord record = CreateRecord(infectionScore: 81, populationKillPending: true);

            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(0, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(0, model.DeadOvernight.CurrentValue);
            Assert.IsFalse(record.populationKillPending);
        }

        [Test]
        public void SimulateDay_SameDayTwice_DoesNotDoubleCount()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 81, populationKillPending: true);

            service.SimulateDay(2, new[] { record });
            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(69, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(1, model.BackgroundDead.CurrentValue);
            Assert.AreEqual(1, model.DeadOvernight.CurrentValue);
        }

        [Test]
        public void Initialize_FromSavedData_RestoresPopulationState()
        {
            PopulationSaveData saved = new PopulationSaveData
            {
                Initialized = true,
                TotalPopulation = 100,
                ContactableDead = 2,
                BackgroundDead = 7,
                DeadOvernight = 3,
                LastSimulatedDay = 5,
            };

            service.Initialize(saved, 30);

            Assert.AreEqual(28, model.ContactableAlive.CurrentValue);
            Assert.AreEqual(63, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(91, model.PopulationAlive.CurrentValue);
            Assert.AreEqual(3, model.DeadOvernight.CurrentValue);
            Assert.AreEqual(5, model.LastSimulatedDay.CurrentValue);
        }

        [Test]
        public void RecordContactableResidentKilled_CountsOnlyOnce()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 0, populationKillPending: true);

            service.RecordContactableResidentKilled(record);
            service.RecordContactableResidentKilled(record);

            Assert.IsTrue(record.populationDeathRecorded);
            Assert.IsFalse(record.hasEnteredCity);
            Assert.IsFalse(record.populationKillPending);
            Assert.AreEqual(29, model.ContactableAlive.CurrentValue);
            Assert.AreEqual(1, model.ContactableDead.CurrentValue);
        }

        [Test]
        public void SimulateDay_KilledPendingMutant_DoesNotKill()
        {
            service.Initialize(null, 30);
            SuspectRecord record = CreateRecord(infectionScore: 81, killed: true, populationKillPending: true);

            service.SimulateDay(2, new[] { record });

            Assert.AreEqual(70, model.BackgroundAlive.CurrentValue);
            Assert.AreEqual(0, model.DeadOvernight.CurrentValue);
        }

        private static SuspectRecord CreateRecord(
            int infectionScore,
            bool killed = false,
            bool populationKillPending = false)
        {
            SuspectData data = ScriptableObject.CreateInstance<SuspectData>();
            SuspectRecord record = new SuspectRecord(data)
            {
                hasEnteredCity = populationKillPending,
                isKilled = killed,
                infectionScore = infectionScore,
                populationKillPending = populationKillPending,
            };
            UnityEngine.Object.DestroyImmediate(data);
            return record;
        }
    }
}
