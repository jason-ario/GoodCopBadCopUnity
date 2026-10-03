using System.Text.RegularExpressions;

/// <summary>
/// Authoring markup for emphasised words in dialogue.
/// <list type="bullet">
/// <item><c>[[phrase]]</c> — tutorial keyword: bold, yellow, nervous wiry tremor.</item>
/// <item><c>{{phrase}}</c> — menace: bold, blood red, harsh shaking with twitches and flicker.</item>
/// </list>
/// <see cref="Apply"/> converts these to TMP rich text wrapped in <c>&lt;link="kw"&gt;</c> /
/// <c>&lt;link="menace"&gt;</c>, which <see cref="TMPWobbleText"/> detects and animates.
/// </summary>
public static class DialogueKeywordMarkup
{
    public const string KeywordLinkId = "kw";
    public const string MenaceLinkId = "menace";

    public const string KeywordOpenTag = "<link=\"" + KeywordLinkId + "\">";
    public const string MenaceOpenTag = "<link=\"" + MenaceLinkId + "\">";
    public const string CloseLinkTag = "</link>";

    /// <summary>Tutorial keyword colour (warm yellow).</summary>
    public const string KeywordColorHex = "FFD23F";
    /// <summary>Menace colour (blood red, still readable on the black bar).</summary>
    public const string MenaceColorHex = "C8191E";

    private static readonly Regex KeywordRegex = new Regex(@"\[\[(.+?)\]\]", RegexOptions.Compiled);
    private static readonly Regex MenaceRegex = new Regex(@"\{\{(.+?)\}\}", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

    /// <summary>True if <paramref name="text"/> contains any <c>[[...]]</c> or <c>{{...}}</c> markup.</summary>
    public static bool HasMarkup(string text) =>
        !string.IsNullOrEmpty(text) &&
        (text.IndexOf("[[", System.StringComparison.Ordinal) >= 0 ||
         text.IndexOf("{{", System.StringComparison.Ordinal) >= 0);

    /// <summary>Converts all markup into bold, coloured, link-tagged rich text. Idempotent.</summary>
    public static string Apply(string text)
    {
        if (!HasMarkup(text)) return text;
        text = KeywordRegex.Replace(text,
            m => $"<b><color=#{KeywordColorHex}>{KeywordOpenTag}{m.Groups[1].Value}{CloseLinkTag}</color></b>");
        text = MenaceRegex.Replace(text,
            m => $"<b><color=#{MenaceColorHex}>{MenaceOpenTag}{m.Groups[1].Value}{CloseLinkTag}</color></b>");
        return text;
    }

    /// <summary>Removes the markup brackets without adding any formatting (plain-text contexts).</summary>
    public static string Strip(string text) =>
        HasMarkup(text) ? MenaceRegex.Replace(KeywordRegex.Replace(text, "$1"), "$1") : text;

    /// <summary>Length of <paramref name="text"/> as rendered, ignoring rich-text tags.</summary>
    public static int VisibleLength(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.IndexOf('<') < 0 ? text.Length : TagRegex.Replace(text, string.Empty).Length);
}
