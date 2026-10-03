using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Authoring markup for emphasised words in dialogue, backed by <see cref="DialogueEmphasisLibrary"/>.
/// <list type="bullet">
/// <item><c>[[phrase]]</c>: the library's bracket default (tutorial keyword).</item>
/// <item><c>{{phrase}}</c>: the library's brace default (menace).</item>
/// <item><c>[[id:phrase]]</c> or <c>{{id:phrase}}</c>: any registered profile, e.g. <c>[[whisper:not alone]]</c>.</item>
/// </list>
/// At most <see cref="MaxPhrasesPerLine"/> phrases are emphasised per line; extras render as plain text
/// (with an editor warning). <see cref="Apply"/> outputs TMP rich text wrapped in
/// <c>&lt;link="emph:id"&gt;</c>, which <see cref="TMPWobbleText"/> animates with that profile.
/// </summary>
public static class DialogueKeywordMarkup
{
    /// <summary>Hard cap on emphasised phrases per dialogue line.</summary>
    public const int MaxPhrasesPerLine = 2;

    public const string LinkPrefix = "<link=\"emph:";
    public const string CloseLinkTag = "</link>";

    private static readonly Regex MarkupRegex = new Regex(@"\[\[(?<b>.+?)\]\]|\{\{(?<c>.+?)\}\}", RegexOptions.Compiled);
    private static readonly Regex IdPrefixRegex = new Regex(@"^(?<id>[A-Za-z][\w-]*):(?<p>.+)$", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static readonly HashSet<string> WarnedLines = new HashSet<string>();
#endif

    /// <summary>True if <paramref name="text"/> contains any <c>[[...]]</c> or <c>{{...}}</c> markup.</summary>
    public static bool HasMarkup(string text) =>
        !string.IsNullOrEmpty(text) &&
        (text.IndexOf("[[", System.StringComparison.Ordinal) >= 0 ||
         text.IndexOf("{{", System.StringComparison.Ordinal) >= 0);

    /// <summary>Number of markup phrases in <paramref name="text"/> (for validation).</summary>
    public static int CountPhrases(string text) => HasMarkup(text) ? MarkupRegex.Matches(text).Count : 0;

    /// <summary>Converts markup into styled, link-tagged rich text, capped at <see cref="MaxPhrasesPerLine"/>.</summary>
    public static string Apply(string text)
    {
        if (!HasMarkup(text)) return text;

        var library = DialogueEmphasisLibrary.Instance;
        int count = 0;

        string result = MarkupRegex.Replace(text, m =>
        {
            Resolve(m, library, out DialogueEmphasisProfile profile, out string phrase);
            count++;

            if (count > MaxPhrasesPerLine)
                return phrase;

            if (profile == null)
                return $"<b>{phrase}</b>";

            return Wrap(profile, phrase);
        });

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (count > MaxPhrasesPerLine && WarnedLines.Add(text))
            Debug.LogWarning($"[DialogueEmphasis] {count} emphasised phrases (max {MaxPhrasesPerLine}); extras shown plain: \"{text}\"");
#endif
        return result;
    }

    /// <summary>Removes markup (and any id prefixes) without adding formatting (plain-text contexts).</summary>
    public static string Strip(string text)
    {
        if (!HasMarkup(text)) return text;
        var library = DialogueEmphasisLibrary.Instance;
        return MarkupRegex.Replace(text, m =>
        {
            Resolve(m, library, out _, out string phrase);
            return phrase;
        });
    }

    /// <summary>Length of <paramref name="text"/> as rendered, ignoring rich-text tags.</summary>
    public static int VisibleLength(string text) =>
        string.IsNullOrEmpty(text) ? 0 : (text.IndexOf('<') < 0 ? text.Length : TagRegex.Replace(text, string.Empty).Length);

    private static void Resolve(Match m, DialogueEmphasisLibrary library, out DialogueEmphasisProfile profile, out string phrase)
    {
        bool brace = m.Groups["c"].Success;
        phrase = brace ? m.Groups["c"].Value : m.Groups["b"].Value;
        profile = null;

        if (library == null) return;

        // Only treat "id:" as a style selector when the id is registered, so "[[Note: x]]" stays literal.
        Match idMatch = IdPrefixRegex.Match(phrase);
        if (idMatch.Success)
        {
            DialogueEmphasisProfile named = library.Get(idMatch.Groups["id"].Value);
            if (named != null)
            {
                profile = named;
                phrase = idMatch.Groups["p"].Value;
                return;
            }
        }

        profile = brace ? library.BraceDefault : library.BracketDefault;
    }

    private static string Wrap(DialogueEmphasisProfile profile, string phrase)
    {
        string hex = ColorUtility.ToHtmlStringRGBA(profile.color);
        string open = (profile.bold ? "<b>" : "") + (profile.italic ? "<i>" : "") +
                      $"<color=#{hex}>{LinkPrefix}{profile.id}\">";
        string close = CloseLinkTag + "</color>" + (profile.italic ? "</i>" : "") + (profile.bold ? "</b>" : "");
        return open + phrase + close;
    }
}
