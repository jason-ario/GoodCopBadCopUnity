using UnityEngine;

namespace GoodCopBadCop.Population
{
    [CreateAssetMenu(fileName = "PopulationConfig", menuName = "GoodCopBadCop/Population/Population Config")]
    public sealed class PopulationConfig : ScriptableObject
    {
        [Min(1)] public int totalPopulation = 100;
        [Min(0)] public Vector2Int backgroundDeathsPerMutantPerDay = new Vector2Int(1, 4);
    }
}
