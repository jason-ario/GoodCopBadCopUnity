using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>Searchable key picker (same widget as Add Component). Browse by sheet, or type to search keys and English text.</summary>
public class LocKeyDropdown : AdvancedDropdown
{
    private class KeyItem : AdvancedDropdownItem
    {
        public readonly string key;
        public KeyItem(string label, string key) : base(label) { this.key = key; }
    }

    private const int SnippetLength = 60;
    private readonly IList<LocEntry> _entries;
    private readonly Action<string> _onPick;

    public LocKeyDropdown(IList<LocEntry> entries, Action<string> onPick) : base(new AdvancedDropdownState())
    {
        _entries = entries;
        _onPick = onPick;
        minimumSize = new Vector2(520, 420);
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem("Strings (" + _entries.Count.ToString("N0") + ")");
        foreach (var sheet in _entries.GroupBy(e => string.IsNullOrEmpty(e.sheet) ? "(no sheet)" : e.sheet).OrderBy(g => g.Key))
        {
            var folder = new AdvancedDropdownItem(sheet.Key + "  (" + sheet.Count() + ")");
            foreach (var e in sheet.OrderBy(e => e.key))
                folder.AddChild(new KeyItem(Label(e), e.key));
            root.AddChild(folder);
        }
        return root;
    }

    // The label holds both the key and a snippet of the English text, so search matches either.
    private static string Label(LocEntry e)
    {
        string src = (e.source ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (src.Length > SnippetLength) src = src.Substring(0, SnippetLength) + "...";
        return string.IsNullOrEmpty(src) ? e.key : e.key + "   \u2014   " + src;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is KeyItem k) _onPick?.Invoke(k.key);
    }
}
