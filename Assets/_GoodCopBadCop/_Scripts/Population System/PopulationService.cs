using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Population
{
    public interface IPopulationService
    {
        void Initialize(PopulationSaveData savedData, int contactableResidentCount);
        void RecordContactableResidentPassed(SuspectRecord record, int activeAnomalyCount);
        void RecordContactableResidentKilled(SuspectRecord record);
        void SimulateDay(int day, IReadOnlyList<SuspectRecord> suspectRecords);
        PopulationSaveData ToSaveData();
    }

    public sealed class PopulationService : IPopulationService
    {
        private readonly PopulationModel model;
        private readonly PopulationConfig config;
        private bool initialized;

        public PopulationService(PopulationModel model, PopulationConfig config)
        {
            this.model = model;
            this.config = config;
        }

        public void Initialize(PopulationSaveData savedData, int contactableResidentCount)
        {
            int totalPopulation = savedData != null && savedData.Initialized
                ? Mathf.Max(1, savedData.TotalPopulation)
                : Mathf.Max(1, config.totalPopulation);
            int contactableTotal = Mathf.Clamp(contactableResidentCount, 0, totalPopulation);
            int backgroundTotal = Mathf.Max(0, totalPopulation - contactableTotal);

            if (savedData != null && savedData.Initialized)
            {
                model.TotalPopulationMutable.Value = Mathf.Max(1, savedData.TotalPopulation);
                model.ContactableDeadMutable.Value = Mathf.Clamp(savedData.ContactableDead, 0, contactableTotal);
                model.ContactableAliveMutable.Value = Mathf.Max(0, contactableTotal - model.ContactableDeadMutable.Value);
                model.BackgroundDeadMutable.Value = Mathf.Clamp(savedData.BackgroundDead, 0, backgroundTotal);
                model.BackgroundAliveMutable.Value = Mathf.Max(0, backgroundTotal - model.BackgroundDeadMutable.Value);
                model.DeadOvernightMutable.Value = Mathf.Max(0, savedData.DeadOvernight);
                model.MutatedOvernightMutable.Value = Mathf.Max(0, savedData.MutatedOvernight);
                model.LastSimulatedDayMutable.Value = Mathf.Max(0, savedData.LastSimulatedDay);
            }
            else
            {
                model.TotalPopulationMutable.Value = totalPopulation;
                model.ContactableAliveMutable.Value = contactableTotal;
                model.ContactableDeadMutable.Value = 0;
                model.BackgroundAliveMutable.Value = backgroundTotal;
                model.BackgroundDeadMutable.Value = 0;
                model.DeadOvernightMutable.Value = 0;
                model.MutatedOvernightMutable.Value = 0;
                model.LastSimulatedDayMutable.Value = 0;
            }

            RecalculateAlive();
            initialized = true;
        }

        public void RecordContactableResidentPassed(SuspectRecord record, int activeAnomalyCount)
        {
            if (record == null || record.isKilled)
                return;

            record.hasEnteredCity = true;
            if (record.IsPopulationMutantByScore || activeAnomalyCount > 10)
                record.populationKillPending = true;
        }

        public void RecordContactableResidentKilled(SuspectRecord record)
        {
            if (!initialized)
                Initialize(null, 0);

            if (record == null || record.populationDeathRecorded)
                return;

            record.populationDeathRecorded = true;
            record.hasEnteredCity = false;
            record.populationKillPending = false;
            model.ContactableDeadMutable.Value = Mathf.Min(
                model.ContactableDeadMutable.Value + 1,
                model.ContactableAliveMutable.Value + model.ContactableDeadMutable.Value);
            model.ContactableAliveMutable.Value = Mathf.Max(0, model.ContactableAliveMutable.Value - 1);
            RecalculateAlive();
        }

        public void SimulateDay(int day, IReadOnlyList<SuspectRecord> suspectRecords)
        {
            if (!initialized)
                Initialize(null, suspectRecords != null ? suspectRecords.Count : 0);

            if (day <= 0 || model.LastSimulatedDayMutable.Value >= day)
                return;

            int deaths = 0;
            int mutatedCount = 0;
            if (suspectRecords != null)
            {
                Vector2Int deathRange = NormalizeDeathRange(config.backgroundDeathsPerMutantPerDay);
                for (int i = 0; i < suspectRecords.Count; i++)
                {
                    SuspectRecord record = suspectRecords[i];
                    if (record == null || record.isKilled || !record.populationKillPending)
                        continue;

                    mutatedCount++;
                    deaths += Random.Range(deathRange.x, deathRange.y + 1);
                    record.populationKillPending = false;
                }
            }

            int appliedDeaths = Mathf.Clamp(deaths, 0, model.BackgroundAliveMutable.Value);
            model.BackgroundAliveMutable.Value -= appliedDeaths;
            model.BackgroundDeadMutable.Value += appliedDeaths;
            model.DeadOvernightMutable.Value = appliedDeaths;
            model.MutatedOvernightMutable.Value = mutatedCount;
            model.LastSimulatedDayMutable.Value = day;
            RecalculateAlive();
        }

        public PopulationSaveData ToSaveData()
        {
            return new PopulationSaveData
            {
                Initialized = initialized,
                TotalPopulation = model.TotalPopulationMutable.Value,
                ContactableDead = model.ContactableDeadMutable.Value,
                BackgroundDead = model.BackgroundDeadMutable.Value,
                DeadOvernight = model.DeadOvernightMutable.Value,
                MutatedOvernight = model.MutatedOvernightMutable.Value,
                LastSimulatedDay = model.LastSimulatedDayMutable.Value,
            };
        }

        private void RecalculateAlive()
        {
            model.PopulationAliveMutable.Value =
                Mathf.Max(0, model.ContactableAliveMutable.Value + model.BackgroundAliveMutable.Value);
        }

        private static Vector2Int NormalizeDeathRange(Vector2Int range)
        {
            int min = Mathf.Max(0, Mathf.Min(range.x, range.y));
            int max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return new Vector2Int(min, max);
        }
    }
}
