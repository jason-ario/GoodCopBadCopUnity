using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Before each player build: increments the last number of PlayerSettings.bundleVersion
/// (when GameSettings.AutoIncrementVersionOnBuild is on) and logs the build channel / Steam App ID.
/// </summary>
public class BuildVersionPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;

    public void OnPreprocessBuild(BuildReport report)
    {
        var settings = GameSettings.Instance;

        if (settings.AutoIncrementVersionOnBuild)
        {
            string previous = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = IncrementPatch(previous);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildVersion] Version {previous} -> {PlayerSettings.bundleVersion}");
        }

        Debug.Log($"[BuildVersion] Building {settings.Channel} (Steam AppId {settings.SteamAppId}), " +
                  $"version {PlayerSettings.bundleVersion}");
    }

    private static string IncrementPatch(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.1.0";

        string[] parts = version.Trim().Split('.');
        if (int.TryParse(parts[parts.Length - 1], out int last))
        {
            parts[parts.Length - 1] = (last + 1).ToString();
            return string.Join(".", parts);
        }

        return version.Trim() + ".1";
    }

    [MenuItem("Good Cop Bad Cop/Build Channel/Playtest (4488670)")]
    private static void SetPlaytest() => SetChannel(GameSettings.BuildChannel.Playtest);

    [MenuItem("Good Cop Bad Cop/Build Channel/Demo (5374510)")]
    private static void SetDemo() => SetChannel(GameSettings.BuildChannel.Demo);

    [MenuItem("Good Cop Bad Cop/Build Channel/Release (3262820)")]
    private static void SetRelease() => SetChannel(GameSettings.BuildChannel.Release);

    [MenuItem("Good Cop Bad Cop/Build Channel/Playtest (4488670)", true)]
    private static bool ValidatePlaytest() => Check(GameSettings.BuildChannel.Playtest, "Playtest (4488670)");

    [MenuItem("Good Cop Bad Cop/Build Channel/Demo (5374510)", true)]
    private static bool ValidateDemo() => Check(GameSettings.BuildChannel.Demo, "Demo (5374510)");

    [MenuItem("Good Cop Bad Cop/Build Channel/Release (3262820)", true)]
    private static bool ValidateRelease() => Check(GameSettings.BuildChannel.Release, "Release (3262820)");

    private static void SetChannel(GameSettings.BuildChannel channel)
    {
        var settings = GameSettings.Instance;
        Undo.RecordObject(settings, "Set Build Channel");
        settings.Channel = channel;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log($"[BuildVersion] Build channel set to {channel} (Steam AppId {settings.SteamAppId})");
    }

    private static bool Check(GameSettings.BuildChannel channel, string label)
    {
        Menu.SetChecked($"Good Cop Bad Cop/Build Channel/{label}", GameSettings.Instance.Channel == channel);
        return true;
    }
}
