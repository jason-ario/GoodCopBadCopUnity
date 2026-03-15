using UnityEngine;

public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;

    [SerializeField, Range(0f, 100f)] float anomalyChance = 30f;
    [SerializeField, Range(0f, 100f)] float multipleAnomalyChance = 25f;
    [SerializeField] int maxAnomalies = 5;

    public bool ShouldHaveAnomalyThisRound => Random.Range(0f, 100f) < anomalyChance;

    void Awake()
    {
        Instance = this;
    }

    public int AnomalyCountThisRound()
    {
        if (!ShouldHaveAnomalyThisRound)
            return 0;

        int count = 1;

        while (count < maxAnomalies && Random.Range(0f, 100f) < multipleAnomalyChance)
        {
            count++;
        }

        return count;
    }
}