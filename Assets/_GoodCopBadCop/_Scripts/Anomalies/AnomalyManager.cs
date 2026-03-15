using UnityEngine;

public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;
    
    [SerializeField] float anomalyChance;
    public bool ShouldHaveAnomalyThisRound =>  Random.Range(0, 100) < anomalyChance;

    
    void Awake()
    {
        Instance = this;
    }
    
}
