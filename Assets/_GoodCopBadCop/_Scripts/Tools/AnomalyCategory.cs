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

    /// <summary>
    /// Resolves the <see cref="AnomalyCategory"/> for a concrete anomaly C# type name (e.g.
    /// "ExpirationDateAnomaly", matching <see cref="ChecklistItem.AnomalyTypeName"/>) by walking
    /// its base-type chain until one of the five category base classes is found. Returns null
    /// when the type name is empty, unresolvable, or does not inherit from a known category.
    /// </summary>
    public static AnomalyCategory? FromAnomalyTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        System.Type type = System.Type.GetType(typeName);
        while (type != null && type != typeof(Anomaly))
        {
            switch (type.Name)
            {
                case "DocumentationAnomaly": return AnomalyCategory.Documentation;
                case "VitalsAnomaly":        return AnomalyCategory.Vitals;
                case "BehaviorAnomaly":      return AnomalyCategory.Behavior;
                case "PhysicalAnomaly":      return AnomalyCategory.Mutations;
                case "SupernaturalAnomaly":  return AnomalyCategory.Supernatural;
            }
            type = type.BaseType;
        }

        return null;
    }
}
