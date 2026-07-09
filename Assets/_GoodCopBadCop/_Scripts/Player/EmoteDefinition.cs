using System;
using UnityEngine;

/// <summary>
/// Data for a single emote entry: display name, animator parameter name, animation duration,
/// and an optional icon sprite shown inside the emote wheel button.
/// </summary>
[Serializable]
public struct EmoteDefinition
{
    [Tooltip("Display name shown above the emote wheel button.")]
    public string Name;

    [Tooltip("Animator bool parameter name that triggers this emote on the body and arms animators.")]
    public string AnimBoolName;

    [Tooltip("How long (seconds) the animation bool stays true before being cleared.")]
    public float Duration;

    [Tooltip("Icon displayed inside the emote wheel button. Leave null for a blank slot.")]
    public Sprite Icon;
}
