using System;
using UnityEngine;

public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;
    public bool mutationAnomaliesLocked;
    public bool behaviorAnomaliesLocked;
    public bool biologicalAnomaliesLocked;
    public bool documentationAnomaliesLocked;
    public bool environmentAnomaliesLocked;
    
    private void Awake()
    {
        Instance = this;
    }
}
