using UnityEngine;

/// <summary>
/// One reusable emphasis style for dialogue words: look (colour, bold, italic) plus a stepped,
/// "wiry" motion. Motion snaps to a new random pose <see cref="stepRate"/> times per second, with
/// each glyph on its own clock, so text trembles and twitches instead of floating smoothly.
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
    public Color color = new Color(1f, 0.824f, 0.247f, 1f);
    public bool bold = true;
    public bool italic;

    [Header("Tremor")]
    [Tooltip("Tremor offset as a fraction of font size. Keep small; it's a nervous twitch, not a wave.")]
    public float jitter = 0.012f;
    [Tooltip("Per-axis multiplier on the tremor. (1, 0.3) = mostly sideways.")]
    public Vector2 jitterAxes = Vector2.one;
    [Tooltip("New tremor poses per second. Higher = more wiry and agitated, lower = creeping.")]
    public float stepRate = 14f;
    [Tooltip("Max random tilt per glyph per step (degrees).")]
    public float rotationJitter = 2f;

    [Header("Twitch")]
    [Tooltip("Chance per glyph per step of a sudden larger jump.")]
    [Range(0f, 1f)] public float twitchChance = 0.015f;
    [Tooltip("Twitch offset as a fraction of font size.")]
    public float twitchAmount = 0.04f;
    [Tooltip("Per-axis multiplier on twitches. (1, 0) = horizontal glitch shears.")]
    public Vector2 twitchAxes = Vector2.one;

    [Header("Typed-In Settle")]
    [Tooltip("Motion multiplier at the moment a glyph is typed in, settling to 1.")]
    public float settleBoost = 2.5f;
    [Tooltip("Seconds for a freshly typed glyph to settle.")]
    public float settleDuration = 0.15f;

    [Header("Flicker")]
    [Tooltip("Chance per glyph per step of briefly dimming, like a failing bulb.")]
    [Range(0f, 1f)] public float flickerChance;
    [Tooltip("How much a flicker dims the glyph (0 = none, 1 = fully).")]
    [Range(0f, 1f)] public float flickerAmount = 0.5f;
    [Tooltip("Flicker fades transparency instead of darkening the colour (ghostly vs. electrical).")]
    public bool flickerFadesAlpha;
}
