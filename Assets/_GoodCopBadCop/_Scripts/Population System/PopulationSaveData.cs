using System;

namespace GoodCopBadCop.Population
{
    [Serializable]
    public class PopulationSaveData
    {
        public bool Initialized;
        public int TotalPopulation = 100;
        public int ContactableDead;
        public int BackgroundDead;
        public int DeadOvernight;
        public int LastSimulatedDay;
    }
}
