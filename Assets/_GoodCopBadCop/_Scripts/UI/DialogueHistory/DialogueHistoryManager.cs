using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static event hub for logging dialogue lines into the dialogue history.
/// Any system can call <see cref="Log"/> to add an entry; listeners
/// (e.g. <see cref="DialogueHistoryUI"/>) subscribe to <see cref="OnEntryAdded"/>.
/// </summary>
public static class DialogueHistoryManager
{
    public enum SpeakerType
    {
        Player,
        Suspect,
        Megaphone
    }

    public readonly struct DialogueEntry
    {
        public readonly SpeakerType SpeakerType;
        public readonly string SpeakerName;
        public readonly string Text;

        public DialogueEntry(SpeakerType speakerType, string speakerName, string text)
        {
            SpeakerType = speakerType;
            SpeakerName = speakerName;
            Text = text;
        }
    }

    // Colours matching the concept image: player = cyan, suspect = orange, megaphone = orange-red
    public static readonly Color PlayerColor   = new Color(0.00f, 0.85f, 0.85f);
    public static readonly Color SuspectColor  = new Color(1.00f, 0.55f, 0.10f);
    public static readonly Color MegaphoneColor = new Color(1.00f, 0.55f, 0.10f);

    private static readonly List<DialogueEntry> _history = new();

    public static IReadOnlyList<DialogueEntry> History => _history;

    /// <summary>Fired on all clients whenever a new entry is appended.</summary>
    public static event Action<DialogueEntry> OnEntryAdded;

    /// <summary>
    /// Appends a new line to the history and notifies all listeners.
    /// Safe to call from any thread, but subscribers execute on whichever thread fires this.
    /// </summary>
    public static void Log(SpeakerType speakerType, string speakerName, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var entry = new DialogueEntry(speakerType, speakerName, text);
        _history.Add(entry);
        OnEntryAdded?.Invoke(entry);
    }

    /// <summary>
    /// Returns the colour associated with a given speaker type.
    /// </summary>
    public static Color GetColor(SpeakerType type)
    {
        return type switch
        {
            SpeakerType.Player    => PlayerColor,
            SpeakerType.Suspect   => SuspectColor,
            SpeakerType.Megaphone => MegaphoneColor,
            _                     => Color.white
        };
    }

    /// <summary>Clears the in-memory history (does not fire OnEntryAdded).</summary>
    public static void Clear() => _history.Clear();
}
