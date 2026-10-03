using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPWobbleText : MonoBehaviour
{
    /// <summary>
    /// Motion settings for one emphasis style. Motion is stepped (snaps to a new random pose
    /// <see cref="stepRate"/> times per second, each glyph on its own offset clock), so the text
    /// trembles and twitches instead of floating smoothly.
    /// </summary>
    [System.Serializable]
    public class EmphasisStyle
    {
        [Tooltip("Tremor offset as a fraction of font size. Keep small; it's a nervous twitch, not a wave.")]
        public float jitter = 0.012f;
        [Tooltip("New tremor poses per second. Higher = more wiry and agitated.")]
        public float stepRate = 14f;
        [Tooltip("Max random tilt per glyph per step (degrees).")]
        public float rotationJitter = 2f;
        [Tooltip("Chance per glyph per step of a sudden larger twitch.")]
        [Range(0f, 1f)] public float twitchChance = 0.015f;
        [Tooltip("Twitch offset as a fraction of font size.")]
        public float twitchAmount = 0.04f;
        [Tooltip("Jitter multiplier at the moment a glyph is typed in, settling to 1.")]
        public float settleBoost = 2.5f;
        [Tooltip("Seconds for a freshly typed glyph to settle.")]
        public float settleDuration = 0.15f;
        [Tooltip("Chance per glyph per step of briefly darkening, like a failing bulb.")]
        [Range(0f, 1f)] public float flickerChance = 0f;
        [Tooltip("How much a flicker darkens the glyph (0 = none, 1 = black).")]
        [Range(0f, 1f)] public float flickerDarken = 0.5f;
    }

    [SerializeField] private TMPWobbleProfile profile;
    [SerializeField] private bool playOnEnable = true;

    [Header("Emphasis ([[keyword]] / {{menace}}, see DialogueKeywordMarkup)")]
    [Tooltip("Animate characters inside <link=\"kw\"> and <link=\"menace\">. Runs independently of the base wobble profile.")]
    [SerializeField] private bool animateEmphasis = true;
    [Tooltip("[[keyword]]: tutorial wording. Uneasy, low-amplitude tremor.")]
    [SerializeField] private EmphasisStyle keywordStyle = new EmphasisStyle();
    [Tooltip("{{menace}}: threatening wording. Harsh shake, twitches, flicker.")]
    [SerializeField] private EmphasisStyle menaceStyle = new EmphasisStyle
    {
        jitter = 0.022f,
        stepRate = 24f,
        rotationJitter = 5f,
        twitchChance = 0.05f,
        twitchAmount = 0.08f,
        settleBoost = 3f,
        settleDuration = 0.2f,
        flickerChance = 0.06f,
        flickerDarken = 0.55f,
    };

    /// <summary>
    /// Global switch for the Text Wobble accessibility setting. When <c>false</c>, every
    /// <see cref="TMPWobbleText"/> instance stops animating and its mesh is reset to the
    /// non-wobbled layout, regardless of individual <see cref="StartWobble"/>/<see cref="StopWobble"/> calls.
    /// Emphasised words keep their static bold/colour styling but stop moving.
    /// </summary>
    public static bool GlobalWobbleEnabled { get; set; } = true;

    private struct EmphasisRange
    {
        public int Start;   // source-string index (inclusive)
        public int End;     // source-string index (exclusive)
        public bool Menace;
    }

    private TextMeshProUGUI tmp;
    private bool isPlaying;
    private bool wasGloballySuppressed;
    private bool meshDirtyFromUs;

    private string lastText = string.Empty;
    private float[] randomPhaseX;
    private float[] randomPhaseY;

    // Emphasis state (source-string index ranges, robust to the typewriter's partial strings).
    private string emphasisScanText;
    private readonly List<EmphasisRange> emphasisRanges = new List<EmphasisRange>();
    private readonly List<float> charAppearTimes = new List<float>();

    private void Awake()
    {
        tmp = GetComponent<TextMeshProUGUI>();
    }

    private void OnEnable()
    {
        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        if (playOnEnable)
            StartWobble();
    }

    private void OnDisable()
    {
        StopWobble(false);
        charAppearTimes.Clear();
    }

    private void LateUpdate()
    {
        if (tmp == null || !tmp.enabled)
            return;

        if (animateEmphasis)
            TrackCharacterAppearance(tmp.textInfo != null ? tmp.textInfo.characterCount : 0);

        string currentText = tmp.text ?? string.Empty;
        bool baseActive = isPlaying && profile != null;
        bool emphasisActive = animateEmphasis && ScanEmphasis(currentText);

        if (!baseActive && !emphasisActive)
        {
            // Emphasis was drawn last frame but is gone now; restore the clean mesh once.
            if (meshDirtyFromUs)
            {
                tmp.ForceMeshUpdate();
                meshDirtyFromUs = false;
            }
            return;
        }

        if (!GlobalWobbleEnabled)
        {
            if (!wasGloballySuppressed)
            {
                tmp.ForceMeshUpdate();
                tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
                wasGloballySuppressed = true;
                meshDirtyFromUs = false;
            }

            return;
        }

        wasGloballySuppressed = false;

        if (baseActive &&
            (currentText != lastText || randomPhaseX == null || randomPhaseY == null ||
             randomPhaseX.Length != currentText.Length || randomPhaseY.Length != currentText.Length))
        {
            RegenerateRandomOffsets();
        }

        ApplyWobble(baseActive, emphasisActive);
    }

    public void SetProfile(TMPWobbleProfile newProfile, bool restartSeeds = true)
    {
        profile = newProfile;

        if (restartSeeds)
            RegenerateRandomOffsets();
    }

    public void StartWobble()
    {
        if (tmp == null)
            tmp = GetComponent<TextMeshProUGUI>();

        isPlaying = true;
        RegenerateRandomOffsets();
    }

    public void StopWobble(bool resetVisuals = true)
    {
        isPlaying = false;

        if (tmp == null)
            return;

        if (resetVisuals)
        {
            tmp.ForceMeshUpdate();
            tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    public void RefreshSeeds()
    {
        RegenerateRandomOffsets();
    }

    private void RegenerateRandomOffsets()
    {
        if (tmp == null || profile == null)
            return;

        lastText = tmp.text ?? string.Empty;

        int length = lastText.Length;
        randomPhaseX = new float[length];
        randomPhaseY = new float[length];

        for (int i = 0; i < length; i++)
        {
            randomPhaseX[i] = Random.Range(profile.randomPhaseMin, profile.randomPhaseMax);
            randomPhaseY[i] = Random.Range(profile.randomPhaseMin, profile.randomPhaseMax);
        }
    }

    /// <summary>
    /// Finds every keyword/menace link span in <paramref name="text"/> as source-index ranges.
    /// An unclosed span (typewriter mid-phrase) runs to the end of the string. Cached per text.
    /// </summary>
    private bool ScanEmphasis(string text)
    {
        if (ReferenceEquals(text, emphasisScanText) || text == emphasisScanText)
            return emphasisRanges.Count > 0;

        emphasisScanText = text;
        emphasisRanges.Clear();
        ScanLinkId(text, DialogueKeywordMarkup.KeywordOpenTag, false);
        ScanLinkId(text, DialogueKeywordMarkup.MenaceOpenTag, true);
        return emphasisRanges.Count > 0;
    }

    private void ScanLinkId(string text, string openTag, bool menace)
    {
        int search = 0;
        while (true)
        {
            int open = text.IndexOf(openTag, search, System.StringComparison.Ordinal);
            if (open < 0) return;

            int start = open + openTag.Length;
            int close = text.IndexOf(DialogueKeywordMarkup.CloseLinkTag, start, System.StringComparison.Ordinal);
            emphasisRanges.Add(new EmphasisRange { Start = start, End = close < 0 ? text.Length : close, Menace = menace });

            if (close < 0) return;
            search = close + DialogueKeywordMarkup.CloseLinkTag.Length;
        }
    }

    /// <summary>Returns the style for a source index, or null if it isn't emphasised.</summary>
    private EmphasisStyle GetEmphasisStyle(int sourceIndex)
    {
        for (int i = 0; i < emphasisRanges.Count; i++)
        {
            EmphasisRange r = emphasisRanges[i];
            if (sourceIndex >= r.Start && sourceIndex < r.End)
                return r.Menace ? menaceStyle : keywordStyle;
        }
        return null;
    }

    /// <summary>
    /// Records when each character first appeared so emphasised glyphs can jolt as they're typed.
    /// The typewriter only ever grows a prefix, so existing indices stay stable; a shrink means new text.
    /// Text that arrives all at once (not typed) gets no settle jolt.
    /// </summary>
    private void TrackCharacterAppearance(int charCount)
    {
        if (charCount < charAppearTimes.Count)
            charAppearTimes.Clear();

        float appearTime = charAppearTimes.Count == 0 && charCount > 1 ? float.NegativeInfinity : Time.unscaledTime;
        while (charAppearTimes.Count < charCount)
            charAppearTimes.Add(appearTime);
    }

    /// <summary>Deterministic, allocation-free hash to [0, 1).</summary>
    private static float Hash01(int a, int b, int salt)
    {
        unchecked
        {
            uint h = (uint)a * 0x8DA6B343u ^ (uint)b * 0xD8163841u ^ (uint)salt * 0xCB1AB31Fu;
            h ^= h >> 13;
            h *= 0x5BD1E995u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    private static float HashSigned(int a, int b, int salt) => Hash01(a, b, salt) * 2f - 1f;

    private void ApplyWobble(bool baseActive, bool emphasisActive)
    {
        tmp.ForceMeshUpdate();

        TMP_TextInfo textInfo = tmp.textInfo;
        int charCount = textInfo.characterCount;
        float now = Time.unscaledTime;
        TrackCharacterAppearance(charCount);

        float baseTime = baseActive ? Time.time * profile.speed : 0f;
        float fontSize = tmp.fontSize;
        bool colorsChanged = false;

        for (int charIndex = 0; charIndex < charCount; charIndex++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
            if (!charInfo.isVisible)
                continue;

            EmphasisStyle style = emphasisActive ? GetEmphasisStyle(charInfo.index) : null;
            if (!baseActive && style == null)
                continue;

            int meshIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;
            Vector3[] vertices = textInfo.meshInfo[meshIndex].vertices;

            Vector3 offset = Vector3.zero;

            if (baseActive)
            {
                float phaseX = (charIndex < randomPhaseX.Length) ? randomPhaseX[charIndex] : 0f;
                float phaseY = (charIndex < randomPhaseY.Length) ? randomPhaseY[charIndex] : 0f;

                float x = Mathf.Sin(baseTime * profile.xFrequencyMultiplier + phaseX) * profile.amountX;
                float y = Mathf.Cos(baseTime * profile.yFrequencyMultiplier + phaseY) * profile.amountY;

                float noiseX = (Mathf.PerlinNoise(charIndex * 0.173f, Time.time * 0.31f * profile.speed) - 0.5f) * 2f * profile.noiseAmount;
                float noiseY = (Mathf.PerlinNoise(charIndex * 0.271f, Time.time * 0.47f * profile.speed) - 0.5f) * 2f * profile.noiseAmount;

                offset += new Vector3(x + noiseX, y + noiseY, 0f);
            }

            if (style == null)
            {
                for (int v = 0; v < 4; v++)
                    vertices[vertexIndex + v] += offset;
                continue;
            }

            // Each glyph snaps to a new pose on its own offset clock, so they never move in unison.
            int step = Mathf.FloorToInt(now * style.stepRate + Hash01(charIndex, 0, 9));

            float sinceAppear = now - charAppearTimes[charIndex];
            float settleT = style.settleDuration > 0f ? Mathf.Clamp01(sinceAppear / style.settleDuration) : 1f;
            float settle = Mathf.Lerp(style.settleBoost, 1f, settleT);

            float amp = style.jitter * fontSize * settle;
            offset += new Vector3(HashSigned(charIndex, step, 1), HashSigned(charIndex, step, 2), 0f) * amp;

            if (Hash01(charIndex, step, 3) < style.twitchChance)
                offset += new Vector3(HashSigned(charIndex, step, 4), HashSigned(charIndex, step, 5), 0f) * (style.twitchAmount * fontSize);

            Quaternion tilt = Quaternion.Euler(0f, 0f, HashSigned(charIndex, step, 6) * style.rotationJitter * settle);
            Vector3 center = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) * 0.5f;
            for (int v = 0; v < 4; v++)
                vertices[vertexIndex + v] = center + tilt * (vertices[vertexIndex + v] - center) + offset;

            if (style.flickerChance > 0f && Hash01(charIndex, step, 7) < style.flickerChance)
            {
                float keep = 1f - style.flickerDarken;
                Color32[] colors = textInfo.meshInfo[meshIndex].colors32;
                for (int v = 0; v < 4; v++)
                {
                    Color32 c = colors[vertexIndex + v];
                    colors[vertexIndex + v] = new Color32((byte)(c.r * keep), (byte)(c.g * keep), (byte)(c.b * keep), c.a);
                }
                colorsChanged = true;
            }
        }

        tmp.UpdateVertexData(colorsChanged
            ? TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32
            : TMP_VertexDataUpdateFlags.Vertices);
        meshDirtyFromUs = true;
    }
}
