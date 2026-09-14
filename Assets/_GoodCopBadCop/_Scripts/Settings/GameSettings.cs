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

    [Header("Guidebook")]
    [Tooltip("When disabled, pressing Tab (or the gamepad Select button) will not open or close the guidebook. " +
             "If the guidebook is already open when this is turned off, it will be force-closed.")]
    [SerializeField] private bool _guidebookEnabled = true;

    [Header("Debug Console")]
    [Tooltip("When disabled, all debug console hotkeys and overlays (DebugConsole hotkeys, CheatConsoleUI [F12], " +
             "AnomalyDebugUI [`], VFXDebugConsoleUI [F11]) are ignored — no need to manually deactivate the Debug Console GameObject.")]
    [SerializeField] private bool _debugConsoleEnabled = true;

    [Header("Survey")]
    [Tooltip("When enabled, the playtest survey opens in the default browser when the application exits.")]
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

    [Header("World Dialogue Overhearing")]
    [Tooltip("How a client who is NOT the engaged player sees another player's world-dialogue conversation " +
             "with an NPC, while within OverhearSubtitleProximity of the speaker. InWorldSubtitles shows a " +
             "floating bubble above the NPC's head. NormalSubtitles shows the regular bottom-of-screen " +
             "subtitle instead. Outside that proximity, the conversation is not shown at all.")]
    [SerializeField] private OverhearSubtitleMode _overhearSubtitleMode = OverhearSubtitleMode.InWorldSubtitles;

    [Tooltip("Max distance (meters) at which a non-engaged player can overhear another player's world-dialogue " +
             "conversation at all, shown either as an in-world bubble or as NormalSubtitles depending on OverhearSubtitleMode.")]
    [SerializeField] private float _overhearSubtitleProximity = 8f;

    /// <summary>How non-engaged players overhear another player's world-dialogue conversation.</summary>
    public OverhearSubtitleMode WorldDialogueOverhearMode
    {
        get => _overhearSubtitleMode;
        set => _overhearSubtitleMode = value;
    }

    /// <summary>Max distance (meters) at which an overheard conversation is shown at all (in either mode).</summary>
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
}
