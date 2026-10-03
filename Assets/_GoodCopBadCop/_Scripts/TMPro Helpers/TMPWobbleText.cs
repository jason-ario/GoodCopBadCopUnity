using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPWobbleText : MonoBehaviour
{
    [SerializeField] private TMPWobbleProfile profile;
    [SerializeField] private bool playOnEnable = true;

    [Header("Keyword Emphasis")]
    [Tooltip("Animate characters inside <link=\"kw\"> (authored as [[phrase]], see DialogueKeywordMarkup). " +
             "Runs independently of the base wobble profile.")]
    [SerializeField] private bool animateKeywords = true;
    [Tooltip("Height of the travelling wave, as a fraction of font size.")]
    [SerializeField] private float keywordWaveHeight = 0.07f;
    [SerializeField] private float keywordWaveSpeed = 6f;
    [Tooltip("Phase offset between neighbouring characters (radians); makes the wave travel through the phrase.")]
    [SerializeField] private float keywordWavePhaseStep = 0.55f;
    [Tooltip("Extra scale at the peak of the pulse (0.08 = 8% bigger).")]
    [SerializeField] private float keywordPulseScale = 0.06f;
    [SerializeField] private float keywordPulseSpeed = 3.5f;
    [Tooltip("Colour the shimmer band blends toward as it sweeps across the phrase.")]
    [SerializeField] private Color keywordShimmerColor = new Color(1f, 0.97f, 0.8f, 1f);
    [SerializeField, Range(0f, 1f)] private float keywordShimmerStrength = 0.6f;
    [SerializeField] private float keywordShimmerSpeed = 1.2f;
    [Tooltip("Scale a keyword character starts at when it is typed in, settling back to 1.")]
    [SerializeField] private float keywordPopScale = 1.6f;
    [SerializeField] private float keywordPopDuration = 0.18f;

    /// <summary>
    /// Global switch for the Text Wobble accessibility setting. When <c>false</c>, every
    /// <see cref="TMPWobbleText"/> instance stops animating and its mesh is reset to the
    /// non-wobbled layout, regardless of individual <see cref="StartWobble"/>/<see cref="StopWobble"/> calls.
    /// Keyword emphasis keeps its static bold/colour styling but stops moving.
    /// </summary>
    public static bool GlobalWobbleEnabled { get; set; } = true;

    private TextMeshProUGUI tmp;
    private bool isPlaying;
    private bool wasGloballySuppressed;
    private bool meshDirtyFromUs;

    private string lastText = string.Empty;
    private float[] randomPhaseX;
    private float[] randomPhaseY;

    // Keyword emphasis state (source-string index ranges, robust to the typewriter's partial strings).
    private string keywordScanText;
    private readonly List<Vector2Int> keywordRanges = new List<Vector2Int>();
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

        if (animateKeywords)
            TrackCharacterAppearance(tmp.textInfo != null ? tmp.textInfo.characterCount : 0);

        string currentText = tmp.text ?? string.Empty;
        bool baseActive = isPlaying && profile != null;
        bool keywordsActive = animateKeywords && ScanKeywords(currentText);

        if (!baseActive && !keywordsActive)
        {
            // Emphasis was drawn last frame but the keyword is gone; restore the clean mesh once.
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

        ApplyWobble(baseActive, keywordsActive);
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
    /// Finds every <c>&lt;link="kw"&gt;</c> span in <paramref name="text"/> as source-index ranges.
    /// An unclosed span (typewriter mid-phrase) runs to the end of the string. Cached per text.
    /// </summary>
    private bool ScanKeywords(string text)
    {
        if (ReferenceEquals(text, keywordScanText) || text == keywordScanText)
            return keywordRanges.Count > 0;

        keywordScanText = text;
        keywordRanges.Clear();

        int search = 0;
        while (true)
        {
            int open = text.IndexOf(DialogueKeywordMarkup.OpenLinkTag, search, System.StringComparison.Ordinal);
            if (open < 0) break;

            int start = open + DialogueKeywordMarkup.OpenLinkTag.Length;
            int close = text.IndexOf(DialogueKeywordMarkup.CloseLinkTag, start, System.StringComparison.Ordinal);
            int end = close < 0 ? text.Length : close;
            keywordRanges.Add(new Vector2Int(start, end));

            if (close < 0) break;
            search = close + DialogueKeywordMarkup.CloseLinkTag.Length;
        }

        return keywordRanges.Count > 0;
    }

    /// <summary>
    /// Records when each character first appeared so keyword characters can pop in as they're typed.
    /// The typewriter only ever grows a prefix, so existing indices stay stable; a shrink means new text.
    /// Text that arrives all at once (not typed) gets no pop.
    /// </summary>
    private void TrackCharacterAppearance(int charCount)
    {
        if (charCount < charAppearTimes.Count)
            charAppearTimes.Clear();

        float appearTime = charAppearTimes.Count == 0 && charCount > 1 ? float.NegativeInfinity : Time.unscaledTime;
        while (charAppearTimes.Count < charCount)
            charAppearTimes.Add(appearTime);
    }

    private bool IsKeywordSourceIndex(int sourceIndex)
    {
        for (int i = 0; i < keywordRanges.Count; i++)
        {
            if (sourceIndex >= keywordRanges[i].x && sourceIndex < keywordRanges[i].y)
                return true;
        }
        return false;
    }

    private void ApplyWobble(bool baseActive, bool keywordsActive)
    {
        tmp.ForceMeshUpdate();

        TMP_TextInfo textInfo = tmp.textInfo;
        int charCount = textInfo.characterCount;
        float now = Time.unscaledTime;
        TrackCharacterAppearance(charCount);

        float baseTime = baseActive ? Time.time * profile.speed : 0f;
        float fontSize = tmp.fontSize;
        Color32 shimmer = keywordShimmerColor;
        bool colorsChanged = false;

        for (int charIndex = 0; charIndex < charCount; charIndex++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
            if (!charInfo.isVisible)
                continue;

            bool isKeyword = keywordsActive && IsKeywordSourceIndex(charInfo.index);
            if (!baseActive && !isKeyword)
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

            if (isKeyword)
            {
                // Travelling wave + breathing scale + pop-in, all around the glyph centre.
                float wave = Mathf.Sin(now * keywordWaveSpeed - charIndex * keywordWavePhaseStep);
                offset.y += wave * keywordWaveHeight * fontSize;

                float pulse = 1f + keywordPulseScale * (0.5f + 0.5f * Mathf.Sin(now * keywordPulseSpeed));
                float popT = keywordPopDuration > 0f ? Mathf.Clamp01((now - charAppearTimes[charIndex]) / keywordPopDuration) : 1f;
                float pop = Mathf.LerpUnclamped(keywordPopScale, 1f, 1f - (1f - popT) * (1f - popT) * (1f - popT));
                float scale = pulse * pop;

                Vector3 center = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) * 0.5f;
                for (int v = 0; v < 4; v++)
                    vertices[vertexIndex + v] = center + (vertices[vertexIndex + v] - center) * scale + offset;

                // Shimmer band sweeping left-to-right across the phrase.
                float band = Mathf.Sin(now * keywordShimmerSpeed * Mathf.PI * 2f - charIndex * 0.45f);
                float blend = Mathf.Clamp01(band) * keywordShimmerStrength;
                if (blend > 0f)
                {
                    Color32[] colors = textInfo.meshInfo[meshIndex].colors32;
                    for (int v = 0; v < 4; v++)
                    {
                        Color32 c = colors[vertexIndex + v];
                        colors[vertexIndex + v] = Color32.Lerp(c, new Color32(shimmer.r, shimmer.g, shimmer.b, c.a), blend);
                    }
                    colorsChanged = true;
                }
            }
            else
            {
                for (int v = 0; v < 4; v++)
                    vertices[vertexIndex + v] += offset;
            }
        }

        tmp.UpdateVertexData(colorsChanged
            ? TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32
            : TMP_VertexDataUpdateFlags.Vertices);
        meshDirtyFromUs = true;
    }
}
