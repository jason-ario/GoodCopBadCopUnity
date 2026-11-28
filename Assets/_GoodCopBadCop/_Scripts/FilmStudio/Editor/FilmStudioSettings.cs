using System;
using UnityEditor;

namespace GoodCopBadCop.FilmStudio
{
    public enum DirectorBackend
    {
        /// <summary>Headless `claude -p` using the user's Claude Code login (Pro/Max subscription).</summary>
        ClaudeCodeCli = 0,
        /// <summary>Anthropic Messages API with an API key.</summary>
        AnthropicApi = 1,
        /// <summary>Bezi chat directs: the window posts a pending request, Bezi answers via Film Studio actions.</summary>
        Bezi = 2,
    }

    /// <summary>
    /// Per-user Film Studio settings, stored in UserSettings/ (not version controlled).
    /// The Anthropic API key is never serialized here; it lives in EditorPrefs or the ANTHROPIC_API_KEY env var.
    /// </summary>
    [FilePath("UserSettings/FilmStudioSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class FilmStudioSettings : ScriptableSingleton<FilmStudioSettings>
    {
        private const string ApiKeyPrefsKey = "FilmStudio.AnthropicKey";
        private const string ApiKeyEnvVar = "ANTHROPIC_API_KEY";

        public DirectorBackend backend = DirectorBackend.ClaudeCodeCli;

        [UnityEngine.Tooltip("Claude Code CLI: empty = auto-detect on PATH / ~/.local/bin.")]
        public string cliPath = "";
        [UnityEngine.Tooltip("Claude Code CLI model alias or id (e.g. opus, sonnet). Empty = CLI default.")]
        public string cliModel = "opus";
        [UnityEngine.Tooltip("Remove ANTHROPIC_API_KEY from the CLI's environment so it always uses your subscription login.")]
        public bool cliForceSubscription = true;
        public int cliTimeoutSec = 600;

        public string model = "";
        public int maxTokens = 16000;
        public float temperature = 0.7f;
        public string filmsFolder = "Assets/_GoodCopBadCop/FilmStudio/Films";
        public string catalogPath = "Assets/_GoodCopBadCop/FilmStudio/StudioCatalog.asset";
        public string recordingsFolder = "Recordings/Shorts";
        public int outputWidth = 1080;
        public int outputHeight = 1920;
        public int fps = 30;
        public float maxDurationSec = 60f;
        public int maxShots = 25;
        public bool usePromptCaching = true;
        [UnityEngine.Tooltip("Film overlay scene (sets = marks + camera occluders, films, film camera). Loaded on top of the main scene in Film Mode.")]
        public string studioScenePath = "Assets/_GoodCopBadCop/_Scenes/FilmStudio.unity";
        [UnityEngine.Tooltip("The game scene that supplies the real environment and lighting in Film Mode. Never saved by Film Studio.")]
        public string mainScenePath = "Assets/_GoodCopBadCop/_Scenes/Main.unity";

        [UnityEngine.Tooltip("Generic animation mode: actors drop their Humanoid avatar, catalog clips are retargeted per rig into " +
                             "Generic clips (cached), an idle base layer runs under every actor, and screenplay customAnims are baked per film.")]
        public bool genericAnimation = true;
        public string genericCacheFolder = "Assets/_GoodCopBadCop/FilmStudio/GenericCache";

        [UnityEngine.Tooltip("Folders scanned (recursively) for cast prefabs: any prefab with a SuspectCharacter, or an Animator plus a " +
                             "SkinnedMeshRenderer. Every SuspectData.CharacterPrefab in the project is always included.")]
        public string[] castSearchFolders = { "Assets/_GoodCopBadCop/_Prefabs/Characters" };
        [UnityEngine.Tooltip("Folders (and their subfolders) never used for cast, e.g. templates and mutated halves of suspects.")]
        public string[] castExcludedFolders =
        {
            "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs/Root Prefab",
            "Assets/_GoodCopBadCop/_Prefabs/Characters/NPCs/Mutated Versions",
            "Assets/_GoodCopBadCop/_Prefabs/Characters/Anomalies",
        };
        [UnityEngine.Tooltip("Add new / drop deleted characters automatically whenever prefabs or SuspectData assets change.")]
        public bool autoSyncCast = true;

        public void SaveSettings() => Save(true);

        public static string ApiKey
        {
            get
            {
                string key = EditorPrefs.GetString(ApiKeyPrefsKey, "");
                if (string.IsNullOrEmpty(key))
                {
                    key = Environment.GetEnvironmentVariable(ApiKeyEnvVar) ?? "";
                }
                return key.Trim();
            }
            set
            {
                if (string.IsNullOrEmpty(value))
                {
                    EditorPrefs.DeleteKey(ApiKeyPrefsKey);
                }
                else
                {
                    EditorPrefs.SetString(ApiKeyPrefsKey, value.Trim());
                }
            }
        }
    }
}
