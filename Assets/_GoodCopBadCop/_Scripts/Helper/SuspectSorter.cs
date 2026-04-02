using System;
using System.Collections.Generic;
using System.Linq;

public static class SuspectSorter
{
    // 🔹 FILTER BY STATUS
    public static List<SuspectRecord> FilterByStatus(List<SuspectRecord> input, CharacterStatus status)
    {
        return input.Where(r => r.Status == status).ToList();
    }

    // 🔹 RECENT EXITS (sorted by time DESC)
    public static List<SuspectRecord> SortByRecentExit(List<SuspectRecord> input, int max = 20)
    {
        return input
            .Where(r => r.LastExitTime != DateTime.MinValue)
            .OrderByDescending(r => r.LastExitTime)
            .Take(max)
            .ToList();
    }

    // 🔹 ALPHABET RANGE (A-F, G-L, etc)
    public static List<SuspectRecord> FilterByLastNameRange(List<SuspectRecord> input, char start, char end)
    {
        return input
            .Where(r =>
            {
                if (string.IsNullOrEmpty(r.Data.LastName)) return false;

                char first = char.ToUpper(r.Data.LastName[0]);
                return first >= start && first <= end;
            })
            .OrderBy(r => r.Data.LastName)
            .ToList();
    }

    // 🔹 FULL ALPHABET SORT (A-Z)
    public static List<SuspectRecord> SortAlphabetical(List<SuspectRecord> input)
    {
        return input
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();
    }
}