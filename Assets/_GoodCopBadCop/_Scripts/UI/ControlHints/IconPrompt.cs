using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Localizable "sentence with an input icon" prompts. A prompt is ONE full string where <see cref="Token"/> ("{0}")
/// marks where the key / button icon goes, e.g. "Hold {0} to rotate" or "{0} to {1}" (verb as {1}). Translators can
/// move the token anywhere in the sentence. At display time the localized string is split at the token: the text
/// before it goes to a label left of the icon, the text after it to a label right of the icon (see
/// <see cref="IconPromptLabels"/>). Icons stay Images, so device switching and rebinding keep working.
/// Strings without the token are treated as "{0} text" (icon first) so old values still render.
/// </summary>
public static class IconPrompt
{
    /// <summary>Icon placeholder inside a prompt template. Extra format arguments start at {1}.</summary>
    public const string Token = "{0}";

    /// <summary>
    /// Translates an English template that came from a [LocalizeField] or <see cref="Loc.N"/> value
    /// (source lookup, so the template's "{0}" survives).
    /// </summary>
    public static string Localize(string englishTemplate) => Loc.TranslateSource(englishTemplate);

    /// <summary>
    /// Translates a code template with extra arguments, keeping the icon token in place:
    /// <c>IconPrompt.Format(Loc.T("{0} to {1}"), verb)</c> gives "{0} to take photo".
    /// </summary>
    public static string Format(string localizedTemplate, params object[] args)
    {
        if (string.IsNullOrEmpty(localizedTemplate) || args == null || args.Length == 0) return localizedTemplate;
        var all = new object[args.Length + 1];
        all[0] = Token;
        Array.Copy(args, 0, all, 1, args.Length);
        try { return string.Format(Loc.Culture, localizedTemplate, all); }
        catch (FormatException)
        {
            Debug.LogWarning("[IconPrompt] Bad format string '" + localizedTemplate + "'.");
            return localizedTemplate;
        }
    }

    /// <summary>
    /// Splits a localized template at the icon token. Both parts are trimmed (the row layout provides the spacing)
    /// and marked verbatim so render-time localization doesn't process the fragments again.
    /// Returns false when the template has no token (then everything is in <paramref name="after"/>).
    /// </summary>
    public static bool Split(string localizedTemplate, out string before, out string after)
    {
        before = string.Empty;
        after = string.Empty;
        if (string.IsNullOrEmpty(localizedTemplate)) return false;

        string text = localizedTemplate.Replace(Loc.Marker, string.Empty);
        int index = text.IndexOf(Token, StringComparison.Ordinal);
        if (index < 0)
        {
            after = Fragment(text);
            return false;
        }

        before = Fragment(text.Substring(0, index));
        after = Fragment(text.Substring(index + Token.Length));
        return true;
    }

    private static string Fragment(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? s : Loc.Verbatim(s);
    }
}

/// <summary>
/// A component that writes <see cref="IconPrompt"/> templates into TMP labels at runtime. The labels' authored text is only an
/// editor placeholder (often a fragment like "to use"); the template field is what gets localized, so the localization scanner
/// skips <see cref="DrivenLabels"/>.
/// </summary>
public interface IIconPromptDriver
{
    System.Collections.Generic.IEnumerable<TMP_Text> DrivenLabels { get; }
}

/// <summary>
/// Drives a "[before] [icon] [after]" prompt row from one localized <see cref="IconPrompt"/> template.
/// The row is the label's parent and must have a HorizontalLayoutGroup. When no before-label is given, one is cloned
/// from the after-label on first use and placed on the icon's left (respects <c>reverseArrangement</c>), so existing
/// "[icon] [label]" rows need no prefab changes. Rows without a layout group show the whole prompt after the icon.
/// Empty labels are hidden.
/// </summary>
public sealed class IconPromptLabels
{
    private readonly TMP_Text _after;
    private readonly Transform _icon;
    private TMP_Text _before;
    private bool _triedCreate;

    /// <param name="afterLabel">Label right of the icon (the row's existing label).</param>
    /// <param name="icon">The icon (or any object inside the row child that holds it).</param>
    /// <param name="beforeLabel">Optional existing label left of the icon. Created on demand when null.</param>
    public IconPromptLabels(TMP_Text afterLabel, Component icon, TMP_Text beforeLabel = null)
    {
        _after = afterLabel;
        _icon = icon != null ? icon.transform : null;
        _before = beforeLabel;
    }

    public IconPromptLabels(TMP_Text afterLabel, GameObject icon, TMP_Text beforeLabel = null)
        : this(afterLabel, icon != null ? icon.transform : null, beforeLabel) { }

    /// <summary>Label left of the icon (null until a template needs it, unless one was passed in).</summary>
    public TMP_Text Before => _before;

    public TMP_Text After => _after;

    /// <summary>Splits <paramref name="localizedTemplate"/> at "{0}" and fills both labels.</summary>
    public void Apply(string localizedTemplate)
    {
        IconPrompt.Split(localizedTemplate, out string before, out string after);
        Set(before, after);
    }

    /// <summary>Sets both labels directly (already localized text). Null/empty hides a label.</summary>
    public void Set(string before, string after)
    {
        before ??= string.Empty;
        if (before.Length > 0) EnsureBefore();

        if (_before != null)
            SetLabel(_before, before);
        else if (before.Length > 0)
            after = string.IsNullOrEmpty(after) ? before : before + " " + after; // no row to clone into: keep the text visible

        if (_after != null) SetLabel(_after, after ?? string.Empty);
    }

    /// <summary>Clears both labels (pooled rows being hidden).</summary>
    public void Clear()
    {
        if (_before != null) _before.text = string.Empty;
        if (_after != null) _after.text = string.Empty;
    }

    private static void SetLabel(TMP_Text label, string value)
    {
        if (label.text != value) label.text = value;
        bool active = value.Length > 0;
        if (label.gameObject.activeSelf != active) label.gameObject.SetActive(active);
    }

    private void EnsureBefore()
    {
        if (_before != null || _triedCreate || _after == null || _icon == null) return;
        _triedCreate = true;

        Transform row = _after.transform.parent;
        Transform iconChild = _icon;
        while (iconChild != null && iconChild.parent != row) iconChild = iconChild.parent;
        HorizontalLayoutGroup layout = row != null ? row.GetComponent<HorizontalLayoutGroup>() : null;
        if (row == null || iconChild == null || layout == null)
        {
            Debug.LogWarning("[IconPromptLabels] '" + _after.name + "' needs a HorizontalLayoutGroup row shared with its icon " +
                             "to show text before the icon; showing the whole prompt after the icon instead.", _after);
            return;
        }

        GameObject clone = UnityEngine.Object.Instantiate(_after.gameObject, row, false);
        clone.name = "Prefix Label";
        // The clone must not be driven by the original label's key.
        if (clone.TryGetComponent(out LocalizedText localized)) localized.key = string.Empty;

        int iconIndex = iconChild.GetSiblingIndex();
        clone.transform.SetSiblingIndex(layout.reverseArrangement ? iconIndex + 1 : iconIndex);

        _before = clone.GetComponent<TMP_Text>();
    }
}
