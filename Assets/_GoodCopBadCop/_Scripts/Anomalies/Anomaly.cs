using UnityEngine;

public class Anomaly : MonoBehaviour
{
    public virtual void ActivateAnomaly()
    {
        Debug.Log("Activated Anomaly: " + gameObject.name);
    }

    public virtual void DeactivateAnomaly()
    {
        Debug.Log("Activated Anomaly: " + gameObject.name);
    }

    /// <summary>
    /// Puts the anomaly into a clean disabled state without any transition effects.
    /// Override in subclasses that drive shader properties to ensure those properties
    /// are zeroed out when the anomaly is not selected for a suspect.
    /// </summary>
    public virtual void InitializeDisabled() { }
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
public class EnvironmentalAnomaly : Anomaly
{
    
}

