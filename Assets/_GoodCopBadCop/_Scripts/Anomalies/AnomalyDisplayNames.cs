using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Single source for player-facing anomaly names (exam checklist, end-of-shift report, debug UI).
/// Explicit names mirror the GuidebookDatabase entries so every surface matches the guidebook;
/// unlisted types fall back to a humanized class name ("HeartRateAnomaly" -> "Heart Rate").
/// </summary>
public static class AnomalyDisplayNames
{
    private static readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Documentation
        { "NameWrongAnomaly",           "Wrong Name" },
        { "BirthDateWrongAnomaly",      "Wrong Date of Birth" },
        { "ExpirationDateAnomaly",      "Expired Document" },
        { "IDNumberWrongAnomaly",       "Wrong ID Number" },
        { "InvalidEntryReasonAnomaly",  "Invalid Entry Reason" },
        { "MissingDocumentAnomaly",     "Missing Documents" },
        { "FakeIdAnomaly",              "Fake ID" },
        { "SexWrongAnomaly",            "Wrong Sex" },
        { "PhotoIDMismatchAnomaly",     "Photo ID Mismatch" },

        // Physical
        { "RandomTumorAnomaly",         "Tumors" },
        { "RandomTentacleAnomaly",      "Tentacles" },
        { "BlackEyesAnomaly",           "Blackened Eyes" },
        { "LesionAnomaly",              "Lesions" },
        { "BlueVeinsAnomaly",           "Blue Veins" },
        { "ExtraLimbAnomaly",           "Extra Limbs" },
        { "VomitAnomaly",               "Vomiting" },
        { "TumorGrowthAnomaly",         "Tumor Growth" },

        // Vitals
        { "HighRadiationAnomaly",       "High Radiation" },
        { "HighTemperatureAnomaly",     "High Temperature" },

        // Behavior
        { "TwitchingAnomaly",           "Twitching" },
        { "LaughingAnomaly",            "Laughing" },
        { "DizzyAnomaly",               "Dizziness" },
        { "ViolentAnomaly",             "Aggression" },
        { "ParanoidAnomaly",            "Paranoia" },
        { "DistractedAnomaly",          "Distracted" },
        { "HyperactiveAnomaly",         "Hyperactive" },
        { "FearfulAnomaly",             "Fearful" },
        { "ConfusedAnomaly",            "Confusion" },

        // Supernatural
        { "FlickeringLightsAnomaly",    "Flickering Lights" },
        { "NotShowingInCameraAnomaly",  "Not On Camera" },
        { "GlitchingAnomaly",           "Glitching" },
        { "DisembodiedVoicesAnomaly",   "Disembodied Voices" },
        { "InsectSwarmAnomaly",         "Insect Swarm" },
        { "RoomTemperatureDropAnomaly", "Cold Room" },
        { "DeadSilenceAnomaly",         "Dead Silence" },
    };

    private static readonly Regex WordBoundary =
        new Regex("(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.Compiled);

    /// <summary>Returns the player-facing name for an anomaly C# type name.</summary>
    public static string Get(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return "Unknown Anomaly";

        if (Names.TryGetValue(typeName, out string name))
            return name;

        const string suffix = "Anomaly";
        if (typeName.Length > suffix.Length && typeName.EndsWith(suffix, StringComparison.Ordinal))
            typeName = typeName.Substring(0, typeName.Length - suffix.Length);

        const string randomPrefix = "Random";
        if (typeName.Length > randomPrefix.Length && typeName.StartsWith(randomPrefix, StringComparison.Ordinal)
            && char.IsUpper(typeName[randomPrefix.Length]))
            typeName = typeName.Substring(randomPrefix.Length);

        return WordBoundary.Replace(typeName, " ");
    }
}
