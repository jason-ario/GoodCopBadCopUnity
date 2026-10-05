using System;
using System.Collections.Generic;

/// <summary>
/// Which unlocked guidebook anomaly entries the local player has already looked at. An entry is
/// "new" while it is unlocked but unseen; it becomes seen when the player opens its section
/// (see <see cref="GuidebookBuilder"/>). Drives the "!" on <see cref="GuidebookIcon"/> and on
/// each <see cref="GuidebookSectionTab"/>, plus the per-page NEW stamp.
///
/// Persisted per save slot through <see cref="SaveDataManager"/>. Without an active slot
/// (network clients, debug skips) it falls back to a session-only set.
/// </summary>
public static class GuidebookSeenState
{
    /// <summary>Raised after one or more entries become seen.</summary>
    public static event Action OnChanged;

    private static readonly HashSet<string> s_sessionSeen = new HashSet<string>(StringComparer.Ordinal);

    private static bool HasSlot => SaveDataManager.Instance != null && SaveDataManager.Instance.ActiveSlot != null;

    public static bool IsSeen(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return true;
        return HasSlot ? SaveDataManager.Instance.IsGuidebookAnomalySeen(typeName) : s_sessionSeen.Contains(typeName);
    }

    /// <summary>Same unlock rule the guidebook uses to decide which pages exist.</summary>
    public static bool IsUnlocked(string typeName) =>
        !string.IsNullOrEmpty(typeName)
        && (AnomalyUnlockManager.Instance == null || AnomalyUnlockManager.Instance.IsAnomalyUnlocked(typeName));

    public static bool IsNew(string typeName) => IsUnlocked(typeName) && !IsSeen(typeName);

    /// <summary>
    /// True if any entry in <paramref name="database"/> that gets a guidebook page (its category
    /// has a section style) is unlocked but not yet seen.
    /// </summary>
    public static bool HasNewEntries(GuidebookDatabase database)
    {
        if (database == null) return false;
        foreach (GuidebookDatabase.AnomalyEntry entry in database.Entries)
            if (entry != null && IsNew(entry.AnomalyTypeName) && HasSection(database, entry))
                return true;
        return false;
    }

    private static bool HasSection(GuidebookDatabase database, GuidebookDatabase.AnomalyEntry entry)
    {
        AnomalyCategory? category = GuidebookDatabase.GetCategory(entry);
        if (category == null) return false;
        foreach (GuidebookDatabase.CategoryStyle style in database.Categories)
            if (style != null && style.Category == category.Value)
                return true;
        return false;
    }

    public static void MarkSeen(IEnumerable<string> typeNames)
    {
        if (typeNames == null) return;

        bool changed;
        if (HasSlot)
        {
            changed = SaveDataManager.Instance.MarkGuidebookAnomaliesSeen(typeNames);
        }
        else
        {
            changed = false;
            foreach (string typeName in typeNames)
                if (!string.IsNullOrEmpty(typeName) && s_sessionSeen.Add(typeName))
                    changed = true;
        }

        if (changed) OnChanged?.Invoke();
    }
}
