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

// MutationAnomaly, BehaviorAnomaly, DocumentationAnomaly, VitalsAnomaly, and SupernaturalAnomaly
// are each defined in their own .cs files so Unity's MonoScript.GetClass() resolves correctly
// for checklist item anomalyTypeReference wiring.

// Kept here for save-data backward compatibility — do not inherit from these in new anomaly scripts.
[System.Serializable]
public class BiologicalAnomaly : Anomaly
{
}

[System.Serializable]
public class EnvironmentalAnomaly : Anomaly
{
}

