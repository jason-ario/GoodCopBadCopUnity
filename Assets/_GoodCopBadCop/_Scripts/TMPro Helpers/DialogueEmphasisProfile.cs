using UnityEngine;

/// <summary>
/// One reusable emphasis style for dialogue words: a flat colour (no gradients, no flicker) plus
/// bold/italic and an optional subtle tremor. The tremor snaps each glyph to a small new offset
/// <see cref="stepRate"/> times per second. Set <see cref="jitter"/> to 0 for colour-only emphasis.
/// Register profiles in <see cref="DialogueEmphasisLibrary"/>; author with <c>[[id:phrase]]</c>.
/// </summary>
[CreateAssetMenu(menuName = "Dialogue/Emphasis Profile")]
public class DialogueEmphasisProfile : ScriptableObject
{
    [Tooltip("Markup id, e.g. 'whisper' for [[whisper:phrase]]. Lowercase, no spaces.")]
    public string id = "keyword";
    [Tooltip("When writers should use this style.")]
    [TextArea(2, 4)] public string usage;

    [Header("Look")]
    [Tooltip("Solid text colour. Alpha is ignored so the phrase never looks faded.")]
    public Color color = new Color(1f, 0.824f, 0.247f, 1f);
    public bool bold = true;
    public bool italic;

    [Header("Tremor")]
    [Tooltip("Tremor offset as a fraction of font size. 0 = static (colour only). Keep very small.")]
    public float jitter = 0.006f;
    [Tooltip("Per-axis multiplier on the tremor. (1, 0.3) = mostly sideways.")]
    public Vector2 jitterAxes = Vector2.one;
    [Tooltip("New tremor poses per second. Lower = calmer.")]
    public float stepRate = 8f;
}
