using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// One reusable emphasis style for dialogue words: a flat colour (never animated) plus bold/italic
/// and a per-glyph motion. Each <see cref="EmphasisMotion"/> has its own movement character so styles
/// read differently even in greyscale. Register profiles in <see cref="DialogueEmphasisLibrary"/>;
/// author with <c>[[id:phrase]]</c>.
/// </summary>
[CreateAssetMenu(menuName = "Dialogue/Emphasis Profile")]
public class DialogueEmphasisProfile : ScriptableObject
{
    public enum EmphasisMotion
    {
        /// <summary>No movement (colour/bold/italic only).</summary>
        Static,
        /// <summary>Smooth ripple travelling across the phrase; crest glyphs lift, tilt and swell slightly.</summary>
        Wave,
        /// <summary>Slow, airy float. Glyphs sway and lean like a breath or candle flame; the phrase breathes in unison.</summary>
        Drift,
        /// <summary>Snapping tremble that spikes on a double heartbeat throb (lub-dub).</summary>
        Shake,
        /// <summary>Glyphs wriggle and shear like something alive, with a lump crawling along under the letters.</summary>
        Squirm
    }

    [Tooltip("Markup id, e.g. 'whisper' for [[whisper:phrase]]. Lowercase, no spaces.")]
    public string id = "keyword";
    [Tooltip("When writers should use this style.")]
    [TextArea(2, 4)] public string usage;

    [Header("Look")]
    [Tooltip("Solid text colour. Alpha is ignored so the phrase never looks faded.")]
    public Color color = new Color(1f, 0.824f, 0.247f, 1f);
    public bool bold = true;
    public bool italic;

    [Header("Motion")]
    public EmphasisMotion motion = EmphasisMotion.Wave;
    [Tooltip("Positional amplitude as a fraction of font size (0.05 = 5% of a glyph's size).")]
    [FormerlySerializedAs("jitter")] public float amplitude = 0.05f;
    [Tooltip("Per-axis multiplier on the positional amplitude.")]
    [FormerlySerializedAs("jitterAxes")] public Vector2 axes = Vector2.one;
    [Tooltip("Cycles per second (Shake: heartbeats per second).")]
    public float speed = 1f;
    [Tooltip("Phase offset between neighbouring glyphs, in radians. Higher = tighter ripple.")]
    public float charPhase = 0.6f;
    [Tooltip("Maximum glyph rotation in degrees.")]
    public float rotation = 4f;
    [Tooltip("Maximum scale change as a fraction (0.1 = up to 10% bigger/smaller).")]
    public float scalePulse = 0.05f;
    [Tooltip("Maximum lean/shear: horizontal shift of a glyph's top relative to its bottom, as a fraction of its height.")]
    public float shear;
    [Tooltip("Shake only: new tremble poses per second.")]
    public float stepRate = 14f;
}
