using System;
using System.Collections.Generic;
using System.Linq;

public static class SuspectSorter
{
    // 🔹 ALPHABET RANGE (A-F, G-L, etc)
    public static List<SuspectData> FilterByLastNameRange(List<SuspectData> input, char start, char end)
    {
        return input
            .Where(r =>
            {
                if (string.IsNullOrEmpty(r.LastName)) return false;

                char first = char.ToUpper(r.LastName[0]);
                return first >= start && first <= end;
            })
            .OrderBy(r => r.LastName)
            .ToList();
    }

    // 🔹 FULL ALPHABET SORT (A-Z)
    public static List<SuspectData> SortAlphabetical(List<SuspectData> input)
    {
        return input
            .OrderBy(r => r.LastName)
            .ThenBy(r => r.FirstName)
            .ToList();
    }
}