using System.Text.RegularExpressions;

/// <summary>
/// Authoring markup for emphasised tutorial keywords in dialogue.
/// Write <c>[[key phrase]]</c> in any dialogue line; <see cref="Apply"/> converts it to bold, yellow
/// TMP rich text wrapped in <c>&lt;link="kw"&gt;</c>, which <see cref="TMPWobbleText"/> detects and
/// animates with its keyword-emphasis motion.
/// </summary>
public static class DialogueKeywordMarkup
{
    public const string LinkId = "kw";
    public const string OpenLinkTag = "<link=\"" + LinkId + "\">";
    public const string CloseLinkTag = "</link>";

    /// <summary>Default emphasis colour (warm yellow).</summary>
    public const string ColorHex = "FFD23F";

    private static readonly Regex MarkupRegex = new Regex(@"\[\[(.+?)\]\]", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>True if <paramref name="text"/> contains at least one <c>[[...]]</c> keyword.</summary>
    public static bool HasMarkup(string text) =>
        !string.IsNullOrEmpty(text) && text.IndexOf("[[", System.StringComparison.Ordinal) >= 0;

    /// <summary>Converts every <c>[[phrase]]</c> into bold, yellow, link-tagged rich text. Idempotent.</summary>
    public static string Apply(string text)
    {
        if (!HasMarkup(text)) return text;
        return MarkupRegex.Replace(text,
            m => $"<b><color=#{ColorHex}>{OpenLinkTag}{m.Groups[1].Value}{CloseLinkTag}</color></b>");
    }

    /// <summary>Removes <c>[[ ]]</c> brackets without adding any formatting (plain-text contexts).</summary>
    public static string Strip(string text) =>
        HasMarkup(text) ? MarkupRegex.Replace(text, "$1") : text;

    /// <summary>Length of <paramref name="text"/> as rendered, ignoring rich-text tags.</summary>
    public static int VisibleLength(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.IndexOf('<') < 0 ? text.Length : TagRegex.Replace(text, string.Empty).Length);
}
