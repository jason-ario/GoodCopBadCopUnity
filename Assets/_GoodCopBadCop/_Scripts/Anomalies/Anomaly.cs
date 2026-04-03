using UnityEngine;

public class Anomaly : MonoBehaviour
{
    [Header("Scoring")]
    [Min(1)] [SerializeField] private int scoreValue = 10;
    [Min(0)] [SerializeField] private int minInfectionScore = 0;
    [Min(0)] [SerializeField] private int maxInfectionScore = 100;
    [Min(1)] [SerializeField] private int selectionWeight = 1;

    public int ScoreValue => scoreValue;
    public int SelectionWeight => selectionWeight;

    public virtual bool CanAppearForScore(int infectionScore)
    {
        return infectionScore >= minInfectionScore && infectionScore <= maxInfectionScore;
    }
    
    public virtual void ActivateAnomaly()
    {
        Debug.Log("Activated Anomaly: " + gameObject.name);
    }

    public virtual void DeactivateAnomaly()
    {
        Debug.Log("Activated Anomaly: " + gameObject.name);
    }
}

[System.Serializable]
public class MutationAnomaly : Anomaly
{
    
}


[System.Serializable]
public class BehaviorAnomaly : Anomaly
{
    
}

[System.Serializable]
public class BiologicalAnomaly : Anomaly
{
    
}

[System.Serializable]
public class DocumentationAnomaly : Anomaly
{
    
}

[System.Serializable]
public class RealityDistortionAnomaly : Anomaly
{
    
}

[System.Serializable]
public class EnvironmentalAnomaly : Anomaly
{
    
}

