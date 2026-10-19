using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes every TMP text in the game follow the current language immediately, including text assigned from code and
/// labels whose <see cref="LocalizedText"/> key isn't in the runtime table yet.
/// <list type="bullet">
/// <item>Each TMP text gets this object as its <see cref="ITextPreprocessor"/>, which runs <see cref="Loc.LocalizeRendered"/>
/// whenever TMP parses the text. The assigned <c>text</c> is never rewritten, so code that reads or appends to it keeps
/// working, and switching back to English is instant.</item>
/// <item>Texts without <see cref="LocalizedText"/> (which handles its own font) and outside <see cref="Subtitles"/> get
/// their role's language font (<see cref="LocalizationSettings.ResolveUntaggedFont"/>) via <see cref="LocalizedFontSwap"/>.</item>
/// <item>Texts are picked up by a full scan on start, scene load and language change, and on their first regenerate
/// (<see cref="TMPro_EventManager.TEXT_CHANGED_EVENT"/>) for objects spawned later.</item>
/// </list>
/// TMP_InputField text components (player-typed text) and texts with another preprocessor are left alone.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class LocalizationTextRuntime : MonoBehaviour, ITextPreprocessor
{
    private sealed class Entry
    {
        public bool Localize;
        public LocalizedFontSwap Font;
    }

    private static LocalizationTextRuntime _instance;

    private readonly Dictionary<TMP_Text, Entry> _tracked = new Dictionary<TMP_Text, Entry>();
    private readonly List<TMP_Text> _pending = new List<TMP_Text>();
    private readonly HashSet<TMP_Text> _pendingSet = new HashSet<TMP_Text>();
    private readonly List<TMP_Text> _dead = new List<TMP_Text>();
    private bool _rescan = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { _instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("[Localization Text Runtime]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<LocalizationTextRuntime>();
    }

    /// <summary>Forces a full rescan and re-render at the end of this frame (e.g. after building UI from code).</summary>
    public static void RefreshAll()
    {
        if (_instance != null) _instance._rescan = true;
    }

    public string PreprocessText(string text) { return Loc.LocalizeRendered(text); }

    private void OnEnable()
    {
        Loc.LanguageChanged += OnLanguageChanged;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        SceneManager.sceneLoaded += OnSceneLoaded;
        _rescan = true;
    }

    private void OnDisable()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnLanguageChanged() { _rescan = true; }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { _rescan = true; }

    // Raised after a text regenerates. Unknown texts are queued and attached in LateUpdate (not mid-rebuild).
    private void OnTextChanged(Object obj)
    {
        if (obj is TMP_Text t && !_tracked.ContainsKey(t) && _pendingSet.Add(t)) _pending.Add(t);
    }

    private void LateUpdate()
    {
        bool english = Loc.CurrentLanguage == Loc.SourceLanguage;

        if (_rescan)
        {
            _rescan = false;
            _pending.Clear();
            _pendingSet.Clear();
            Prune();
            foreach (var t in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Register(t, true);
            return;
        }

        if (_pending.Count == 0) return;
        for (int i = 0; i < _pending.Count; i++) Register(_pending[i], !english);
        _pending.Clear();
        _pendingSet.Clear();
    }

    private void Register(TMP_Text t, bool rerender)
    {
        if (t == null) return;
        if (!_tracked.TryGetValue(t, out var entry))
        {
            entry = CreateEntry(t);
            _tracked[t] = entry;
        }

        if (entry.Font != null) ApplyFont(t, entry.Font);
        if (rerender && entry.Localize) t.havePropertiesChanged = true;
    }

    private Entry CreateEntry(TMP_Text t)
    {
        var entry = new Entry();
        var input = t.GetComponentInParent<TMP_InputField>(true);
        bool playerTyped = input != null && input.textComponent == t;

        if (!playerTyped)
        {
            if (t.textPreprocessor == null) t.textPreprocessor = this;
            entry.Localize = ReferenceEquals(t.textPreprocessor, this);
        }

        bool ownsFont = playerTyped || t.GetComponent<LocalizedText>() != null || t.GetComponentInParent<Subtitles>(true) != null;
        if (!ownsFont) entry.Font = new LocalizedFontSwap();
        return entry;
    }

    private static void ApplyFont(TMP_Text t, LocalizedFontSwap swap)
    {
        var settings = LocalizationSettings.Instance;
        if (swap.OriginalFont == null) swap.Capture(t);
        var lang = settings != null ? settings.FontLanguage(Loc.CurrentLanguage) : null;
        swap.Apply(t, lang != null ? settings.ResolveUntaggedFont(lang, swap.OriginalFont) : null);
    }

    private void Prune()
    {
        _dead.Clear();
        foreach (var kv in _tracked) if (kv.Key == null) _dead.Add(kv.Key);
        foreach (var t in _dead) _tracked.Remove(t);
    }
}
