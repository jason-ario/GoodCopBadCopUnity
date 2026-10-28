using UnityEngine;

/// <summary>
/// Internal, developer-facing game settings toggles — editable in the Inspector
/// (on the asset) or at runtime via code. Backed by a single ScriptableObject asset
/// loaded through Resources, so any script can flip these flags without needing
/// scene wiring or manually enabling/disabling GameObjects.
/// </summary>
/// <remarks>
/// The asset lives at Assets/_GoodCopBadCop/Resources/GameSettings.asset.
/// Select it in the Project window to toggle these values from the Inspector.
/// </remarks>
[CreateAssetMenu(fileName = "GameSettings", menuName = "Good Cop Bad Cop/Game Settings")]
public class GameSettings : ScriptableObject
{
    private const string ResourcePath = "GameSettings";
    private static GameSettings _instance;

    /// <summary>
    /// Global accessor. Loads the Resources/GameSettings asset on first access.
    /// Falls back to an in-memory instance with default values if the asset is missing.
    /// </summary>
    public static GameSettings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<GameSettings>(ResourcePath);
                if (_instance == null)
                {
                    _instance = CreateInstance<GameSettings>();
                    Debug.LogWarning(
                        $"[GameSettings] No asset found at Resources/{ResourcePath}. " +
                        "Using in-memory defaults. Create one via Assets > Create > Good Cop Bad Cop > Game Settings.");
                }
            }
            return _instance;
        }
    }

    /// <summary>Which Steam app this build targets. Drives the Steam App ID used by the network transport.</summary>
    public enum BuildChannel
    {
        Playtest,
        Demo,
        Release
    }

    public const uint PlaytestAppId = 4488670;
    public const uint DemoAppId = 5374510;
    public const uint ReleaseAppId = 3262820;

    [Header("Build")]
    [Tooltip("Steam app this build targets. Overrides the App ID on every FacepunchTransport at startup " +
             "(Playtest = 4488670, Demo = 5374510, Release = 3262820). All players must run the same channel " +
             "to find each other's lobbies.")]
    [SerializeField] private BuildChannel _buildChannel = BuildChannel.Playtest;

    [Tooltip("Text shown before the version number on the main menu, e.g. \"V. Alpha\" -> \"V. Alpha 0.1.12\".")]
    [SerializeField] private string _versionLabelPrefix = "V. Alpha";

    [Tooltip("Append the build channel to the main menu version label for non-Release builds, e.g. \"(Playtest)\".")]
    [SerializeField] private bool _showChannelInVersionLabel = true;

    [Tooltip("When enabled, every player build increments the last number of Player Settings > Version " +
             "(0.1.11 -> 0.1.12) before building.")]
    [SerializeField] private bool _autoIncrementVersionOnBuild = true;

    public BuildChannel Channel
    {
        get => _buildChannel;
        set => _buildChannel = value;
    }

    /// <summary>True when this build targets the Steam demo (<see cref="BuildChannel.Demo"/>).</summary>
    public bool IsDemo => _buildChannel == BuildChannel.Demo;

    /// <summary>Steam App ID for the current <see cref="Channel"/>.</summary>
    public uint SteamAppId => GetSteamAppId(_buildChannel);

    public bool AutoIncrementVersionOnBuild => _autoIncrementVersionOnBuild;

    public static uint GetSteamAppId(BuildChannel channel) => channel switch
    {
        BuildChannel.Demo => DemoAppId,
        BuildChannel.Release => ReleaseAppId,
        _ => PlaytestAppId
    };

    /// <summary>Main menu version text, e.g. "V. Alpha 0.1.12 (Playtest)".</summary>
    public string GetVersionDisplayString()
    {
        string text = string.IsNullOrWhiteSpace(_versionLabelPrefix)
            ? Application.version
            : $"{_versionLabelPrefix} {Application.version}";
        if (_showChannelInVersionLabel && _buildChannel != BuildChannel.Release)
            text += $" ({_buildChannel})";
        return text;
    }

    [Header("Guidebook")]
    [Tooltip("When disabled, pressing Tab (or the gamepad Select button) will not open or close the guidebook. " +
             "If the guidebook is already open when this is turned off, it will be force-closed.")]
    [SerializeField] private bool _guidebookEnabled = true;

    [Header("Debug Console")]
    [Tooltip("When disabled, all debug console hotkeys and overlays (DebugConsole hotkeys, CheatConsoleUI [F12], " +
             "AnomalyDebugUI [`]) are ignored — no need to manually deactivate the Debug Console GameObject. " +
             "Does not affect the Trailer Console [F11], which has its own flag.")]
    [SerializeField] private bool _debugConsoleEnabled = true;

    [Header("Trailer Console")]
    [Tooltip("When enabled, F11 opens the Trailer Console (hide HUD, free camera with bookmarks/dolly, time scale, " +
             "environment preset lock). Independent of Debug Console so a trailer build can disable gameplay cheats " +
             "but keep this. Works in release builds too — turn it off before public builds.")]
    [SerializeField] private bool _trailerConsoleEnabled = true;

    [Header("Inventory")]
    [Tooltip("When enabled, the player's hotbar has 3 slots instead of 2 (key 3 selects the third slot) and a " +
             "third slot widget is added to the HUD at runtime. Read when the player spawns, so change it " +
             "before starting/joining a session. All peers should use the same value.")]
    [SerializeField] private bool _threeInventorySlots = false;

    /// <summary>Whether the hotbar uses 3 slots instead of 2. Applied when the player spawns.</summary>
    public bool ThreeInventorySlots
    {
        get => _threeInventorySlots;
        set => _threeInventorySlots = value;
    }

    /// <summary>Number of hotbar slots implied by <see cref="ThreeInventorySlots"/> (2 or 3).</summary>
    public int InventorySlotCount => _threeInventorySlots ? 3 : 2;

    [Header("Localization")]
    [Tooltip("When enabled, Settings > Gameplay > Language offers \"Pseudolocalization\" (accented, expanded, [bracketed] " +
             "text that follows Unity's pseudo-localization guidelines) in every build type. When disabled, the option is " +
             "hidden and a saved pseudolocalization choice falls back to the default language.")]
    [SerializeField] private bool _pseudoLocalizationEnabled = true;

    /// <summary>Whether the "Pseudolocalization" language is selectable (Settings menu, cheat console, saved preference).</summary>
    public bool PseudoLocalizationEnabled
    {
        get => _pseudoLocalizationEnabled;
        set => _pseudoLocalizationEnabled = value;
    }

    [Header("Survey")]
    [Tooltip("When enabled, the playtest survey opens in the default browser when the application exits. " +
             "Always skipped in Demo builds (Build Channel = Demo).")]
    [SerializeField] private bool _openSurveyOnGameExit = true;

    /// <summary>Whether the playtest survey opens automatically when the application exits.</summary>
    public bool OpenSurveyOnGameExit
    {
        get => _openSurveyOnGameExit;
        set => _openSurveyOnGameExit = value;
    }

    /// <summary>
    /// How a non-engaged client overhears a <see cref="SuspectWorldDialogue"/> conversation that
    /// another player is having with an NPC (see <see cref="SpeakingInteraction.SayWorldDialogue"/>).
    /// </summary>
    public enum OverhearSubtitleMode
    {
        /// <summary>Floating bubble anchored above the speaking NPC's head, shown only while within <see cref="OverhearSubtitleProximity"/>.</summary>
        InWorldSubtitles,

        /// <summary>Standard bottom-of-screen subtitle, shown only while within <see cref="OverhearSubtitleProximity"/>.</summary>
        NormalSubtitles
    }

    [Header("Dialogue Overhearing")]
    [Tooltip("How a client who is NOT the engaged player sees another player's world-dialogue conversation " +
             "with an NPC, while within OverhearSubtitleProximity of the speaker. InWorldSubtitles shows a " +
             "floating bubble above the NPC's head. NormalSubtitles shows the regular bottom-of-screen " +
             "subtitle instead. Outside that proximity, the conversation is not shown at all.")]
    [SerializeField] private OverhearSubtitleMode _overhearSubtitleMode = OverhearSubtitleMode.InWorldSubtitles;

    [Tooltip("Max distance (meters) from a speaker at which a non-participating player can see that conversation's " +
             "subtitles at all. Applies to every conversation type: booth/suspect dialogue and player choices, " +
             "scripted dialogue, world dialogue, in-world subtitle bubbles, and NPC barks. Players outside this " +
             "range see no subtitles for that conversation. Evaluated through OverhearRange.")]
    [Min(0f)]
    [SerializeField] private float _overhearSubtitleProximity = 8f;

    /// <summary>How non-engaged players overhear another player's world-dialogue conversation.</summary>
    public OverhearSubtitleMode WorldDialogueOverhearMode
    {
        get => _overhearSubtitleMode;
        set => _overhearSubtitleMode = value;
    }

    /// <summary>
    /// Max distance (meters) at which a non-participant sees any conversation's subtitles (booth,
    /// scripted, world dialogue, in-world bubbles, barks). Query through <see cref="OverhearRange"/>.
    /// </summary>
    public float OverhearSubtitleProximity
    {
        get => _overhearSubtitleProximity;
        set => _overhearSubtitleProximity = value;
    }

    public bool GuidebookEnabled
    {
        get => _guidebookEnabled;
        set => _guidebookEnabled = value;
    }

    /// <summary>Whether debug console hotkeys and overlays respond to input.</summary>
    public bool DebugConsoleEnabled
    {
        get => _debugConsoleEnabled;
        set => _debugConsoleEnabled = value;
    }

    /// <summary>Whether the F11 Trailer Console responds to input. Independent of <see cref="DebugConsoleEnabled"/>.</summary>
    public bool TrailerConsoleEnabled
    {
        get => _trailerConsoleEnabled;
        set => _trailerConsoleEnabled = value;
    }
}
