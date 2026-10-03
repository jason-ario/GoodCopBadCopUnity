using UnityEngine;

public class Anomaly : MonoBehaviour
{
    [Tooltip("Player-facing name shown in the end-of-shift report details. Leave empty to derive it from the class name (e.g. BlackEyesAnomaly -> \"Black Eyes\").")]
    [SerializeField] private string reportDisplayName;

    /// <summary>Name shown for this anomaly on the end-of-shift report detail popup.</summary>
    public string ReportDisplayName => string.IsNullOrWhiteSpace(reportDisplayName)
        ? ShiftAnomalyCategories.PrettifyTypeName(GetType().Name)
        : reportDisplayName;

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

// PhysicalAnomaly, BehaviorAnomaly, DocumentationAnomaly, VitalsAnomaly, and SupernaturalAnomaly
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

