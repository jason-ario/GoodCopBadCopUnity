/// <summary>
/// The five anomaly categories that map directly to the five checklist checkboxes.
/// Each value corresponds to one of the five Anomaly base-class names used by AnomalyController.
/// </summary>
public enum AnomalyCategory
{
    Documentation,
    Vitals,
    Behavior,
    Mutations,
    Supernatural
}

public static class AnomalyCategoryExtensions
{
    /// <summary>
    /// Returns the C# base-class name that the checklist and AnomalyController use
    /// to identify this category (e.g. "PhysicalAnomaly").
    /// </summary>
    public static string ToTypeName(this AnomalyCategory category) => category switch
    {
        AnomalyCategory.Documentation => "DocumentationAnomaly",
        AnomalyCategory.Vitals        => "VitalsAnomaly",
        AnomalyCategory.Behavior      => "BehaviorAnomaly",
        AnomalyCategory.Mutations     => "PhysicalAnomaly",
        AnomalyCategory.Supernatural  => "SupernaturalAnomaly",
        _                             => string.Empty
    };
}
