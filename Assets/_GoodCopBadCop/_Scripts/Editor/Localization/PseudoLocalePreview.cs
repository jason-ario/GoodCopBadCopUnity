using System.Text;
using Bezi;

/// <summary>Editor-side preview of pseudolocale output and its QC result, without entering Play Mode.</summary>
public static class PseudoLocalePreview
{
    [BeziAction(
        "Preview pseudolocalized text. code: pseudolocale code (pseudo, pseudo-latincyr, pseudo-cjk). " +
        "texts: English source strings separated by '\\n|\\n'. Returns each output plus Verify() issues.",
        IsReadOnly = true)]
    public static string Preview(string code, string texts)
    {
        var locale = PseudoLocalizer.Get(code);
        if (locale == null) return "Unknown pseudolocale '" + code + "'.";
        var settings = LocalizationSettings.Instance;
        float expansion = settings != null ? settings.pseudoExpansion : 0.4f;

        var sb = new StringBuilder();
        foreach (string source in (texts ?? "").Split(new[] { "\n|\n" }, System.StringSplitOptions.None))
        {
            string output = locale.Generate(source, source, expansion);
            var issues = locale.Verify(source, output, source, expansion);
            sb.AppendLine(source + "  =>  " + output + (issues.Count == 0 ? "  [OK]" : "  [ISSUES: " + string.Join("; ", issues) + "]"));
        }
        return sb.ToString();
    }

    [BeziAction("Play Mode only. Switches the game language with Loc.SetLanguage (codes: en, ru, zh-Hans, pseudo, ...). Returns the active language.")]
    public static string SetLanguage(string code)
    {
        if (!UnityEngine.Application.isPlaying) return "Enter Play Mode first.";
        Loc.SetLanguage(code);
        return Loc.CurrentLanguage;
    }

    [BeziAction(
        "Play Mode only. Reports how many active TMP texts render localized and samples of what they render " +
        "(GetParsedText) with their fonts. pathFilter (optional) limits samples to hierarchy paths containing it.",
        IsReadOnly = true)]
    public static string Report(string pathFilter = "", int maxSamples = 25)
    {
        if (!UnityEngine.Application.isPlaying) return "Enter Play Mode first.";

        var texts = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(UnityEngine.FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None);
        int hooked = 0, withLetters = 0, changed = 0;
        var sb = new StringBuilder();
        int samples = 0;
        foreach (var t in texts)
        {
            if (t.textPreprocessor is LocalizationTextRuntime) hooked++;
            string raw = t.text ?? "";
            if (!System.Text.RegularExpressions.Regex.IsMatch(raw, "[A-Za-z]")) continue;
            withLetters++;
            t.ForceMeshUpdate();
            string parsed = t.GetParsedText();
            bool differs = parsed.Replace(Loc.Marker, "") != System.Text.RegularExpressions.Regex.Replace(raw, "<[^>]+>", "").Replace(Loc.Marker, "");
            if (differs) changed++;
            string path = PathOf(t.transform);
            if (samples < maxSamples && (string.IsNullOrEmpty(pathFilter) || path.Contains(pathFilter)))
            {
                samples++;
                string shown = parsed.Replace(Loc.Marker, "").Replace("\n", " ");
                if (shown.Length > 70) shown = shown.Substring(0, 70) + "…";
                sb.AppendLine((differs ? "[changed] " : "[same]    ") + path + " | font=" + (t.font != null ? t.font.name : "-") + " | " + shown);
            }
        }
        return "Language=" + Loc.CurrentLanguage + " activeTMP=" + texts.Length + " hooked=" + hooked +
               " withLetters=" + withLetters + " renderedDifferently=" + changed + "\n" + sb;
    }

    private static string PathOf(UnityEngine.Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return "/" + p;
    }
}
