using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Data for a single emote entry: display name, animator trigger name, animation duration,
/// and an optional icon sprite shown inside the emote wheel button.
/// </summary>
[Serializable]
public struct EmoteDefinition
{
    [Tooltip("Display name shown above the emote wheel button.")]
    public string Name;

    [Tooltip("Animator trigger parameter name that plays this emote on the body and arms animators.")]
    [FormerlySerializedAs("AnimBoolName")]
    public string AnimTriggerName;

    [Tooltip("How long (seconds) the upper-body layer is held at full weight and further emotes are blocked. " +
             "Should roughly match the emote clip length; the animator itself exits on exit time.")]
    public float Duration;

    [Tooltip("Icon displayed inside the emote wheel button. Leave null for a blank slot.")]
    public Sprite Icon;
}
