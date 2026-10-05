using System.Collections.Generic;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPWobbleText : MonoBehaviour
{
    [SerializeField] private TMPWobbleProfile profile;
    [SerializeField] private bool playOnEnable = true;

    [Header("Dialogue Emphasis (see DialogueKeywordMarkup / DialogueEmphasisLibrary)")]
    [Tooltip("Animate characters inside <link=\"emph:id\"> using that id's DialogueEmphasisProfile. " +
             "Runs independently of the base wobble profile.")]
    [SerializeField] private bool animateEmphasis = true;
    [Tooltip("Scales every emphasis profile's motion on this text (1 = as authored).")]
    [SerializeField, Range(0f, 2f)] private float emphasisIntensity = 1f;

    /// <summary>
    /// Global switch for the Text Wobble accessibility setting. When <c>false</c>, every
    /// <see cref="TMPWobbleText"/> instance stops animating and its mesh is reset to the
    /// non-wobbled layout, regardless of individual <see cref="StartWobble"/>/<see cref="StopWobble"/> calls.
    /// Emphasised words keep their static colour/bold/italic styling but stop moving.
    /// </summary>
    public static bool GlobalWobbleEnabled { get; set; } = true;

    private struct EmphasisRange
    {
        public int Start;   // source-string index (inclusive)
        public int End;     // source-string index (exclusive)
        public DialogueEmphasisProfile Profile;
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
    }

    private void LateUpdate()
    {
        if (tmp == null || !tmp.enabled)
            return;

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
    /// Finds every <c>&lt;link="emph:id"&gt;</c> span in <paramref name="text"/> as source-index ranges
    /// paired with that id's profile. An unclosed span (typewriter mid-phrase) runs to the end of the
    /// string. Unknown ids are ignored. Cached per text.
    /// </summary>
    private bool ScanEmphasis(string text)
    {
        if (ReferenceEquals(text, emphasisScanText) || text == emphasisScanText)
            return emphasisRanges.Count > 0;

        emphasisScanText = text;
        emphasisRanges.Clear();

        if (text.IndexOf(DialogueKeywordMarkup.LinkPrefix, System.StringComparison.Ordinal) < 0)
            return false;

        var library = DialogueEmphasisLibrary.Instance;
        if (library == null)
            return false;

        string prefix = DialogueKeywordMarkup.LinkPrefix;
        int search = 0;
        while (true)
        {
            int open = text.IndexOf(prefix, search, System.StringComparison.Ordinal);
            if (open < 0) break;

            int idStart = open + prefix.Length;
            int idEnd = text.IndexOf("\">", idStart, System.StringComparison.Ordinal);
            if (idEnd < 0) break; // tag itself not fully present yet

            int start = idEnd + 2;
            int close = text.IndexOf(DialogueKeywordMarkup.CloseLinkTag, start, System.StringComparison.Ordinal);

            DialogueEmphasisProfile p = library.Get(text.Substring(idStart, idEnd - idStart));
            if (p != null)
                emphasisRanges.Add(new EmphasisRange { Start = start, End = close < 0 ? text.Length : close, Profile = p });

            if (close < 0) break;
            search = close + DialogueKeywordMarkup.CloseLinkTag.Length;
        }

        return emphasisRanges.Count > 0;
    }

    /// <summary>Returns the profile for a source index, or null if it isn't emphasised.</summary>
    private DialogueEmphasisProfile GetEmphasisProfile(int sourceIndex)
    {
        for (int i = 0; i < emphasisRanges.Count; i++)
        {
            EmphasisRange r = emphasisRanges[i];
            if (sourceIndex >= r.Start && sourceIndex < r.End)
                return r.Profile;
        }
        return null;
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

        float baseTime = baseActive ? Time.time * profile.speed : 0f;
        float fontSize = tmp.fontSize;

        for (int charIndex = 0; charIndex < charCount; charIndex++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
            if (!charInfo.isVisible)
                continue;

            DialogueEmphasisProfile style = emphasisActive ? GetEmphasisProfile(charInfo.index) : null;
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

            if (style == null || style.motion == DialogueEmphasisProfile.EmphasisMotion.Static)
            {
                for (int v = 0; v < 4; v++)
                    vertices[vertexIndex + v] += offset;
                continue;
            }

            // Per-glyph emphasis motion: offset + rotation + scale + lean around the glyph centre.
            // Vertex colours are never touched, so the phrase keeps one flat colour.
            EvaluateEmphasis(style, charIndex, now, fontSize * emphasisIntensity,
                             out Vector2 emphOffset, out float rotDeg, out float scale, out float lean);
            offset += (Vector3)emphOffset;

            Vector3 center = (vertices[vertexIndex] + vertices[vertexIndex + 2]) * 0.5f;
            Quaternion rot = Quaternion.Euler(0f, 0f, rotDeg);
            for (int v = 0; v < 4; v++)
            {
                Vector3 local = vertices[vertexIndex + v] - center;
                local.x += lean * local.y;
                local = rot * (local * scale);
                vertices[vertexIndex + v] = center + local + offset;
            }
        }

        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        meshDirtyFromUs = true;
    }

    private const float Tau = Mathf.PI * 2f;

    private static float Bump(float x, float width) => Mathf.Exp(-(x * x) / (width * width));

    /// <summary>
    /// Computes one glyph's emphasis pose. <paramref name="sizeScale"/> is font size times intensity,
    /// so amplitudes stay proportional to the text. Rotation/scale/lean scale with intensity too.
    /// </summary>
    private void EvaluateEmphasis(DialogueEmphasisProfile s, int i, float now, float sizeScale,
                                  out Vector2 offset, out float rotDeg, out float scale, out float lean)
    {
        float k = emphasisIntensity;
        float amp = s.amplitude * sizeScale;
        float t = now * s.speed;
        float p = i * s.charPhase;

        offset = Vector2.zero;
        rotDeg = 0f;
        scale = 1f;
        lean = 0f;

        switch (s.motion)
        {
            case DialogueEmphasisProfile.EmphasisMotion.Wave:
            {
                // Travelling ripple: glyph lifts, tilts along the slope, swells at the crest.
                float u = Tau * t - p;
                float w = Mathf.Sin(u);
                float slope = Mathf.Cos(u);
                offset = new Vector2(slope * amp * s.axes.x, w * amp * s.axes.y);
                rotDeg = -slope * s.rotation * k;
                scale = 1f + s.scalePulse * k * Mathf.Max(0f, w);
                break;
            }

            case DialogueEmphasisProfile.EmphasisMotion.Drift:
            {
                // Airy float: smooth sway + organic noise, flame-like lean, unison breathing.
                float u = Tau * t - p;
                float sway = Mathf.Sin(u);
                float nx = Mathf.PerlinNoise(i * 0.37f + 11.3f, t * 0.9f) * 2f - 1f;
                float ny = Mathf.PerlinNoise(i * 0.37f + 47.1f, t * 0.9f) * 2f - 1f;
                float rise = Mathf.Sin(Tau * t * 0.6f - p * 0.7f + 1.3f);

                offset = new Vector2((0.65f * sway + 0.35f * nx) * amp * s.axes.x,
                                     (0.55f * rise + 0.45f * ny) * amp * s.axes.y);
                rotDeg = -(0.7f * sway + 0.3f * nx) * s.rotation * k;
                lean = (0.75f * sway + 0.25f * nx) * s.shear * k;
                scale = 1f + s.scalePulse * k * Mathf.Sin(Tau * t * 0.5f);
                break;
            }

            case DialogueEmphasisProfile.EmphasisMotion.Shake:
            {
                // Lub-dub heartbeat drives a throb; tremble is constant but spikes on each beat.
                float beatPhase = t - Mathf.Floor(t);
                float beat = Mathf.Max(Bump(beatPhase, 0.07f), Bump(1f - beatPhase, 0.07f))
                           + 0.6f * Bump(beatPhase - 0.22f, 0.07f);
                float tremble = 0.4f + 1.2f * beat;

                float rate = Mathf.Max(0.01f, s.stepRate);
                int step = Mathf.FloorToInt(now * rate + Hash01(i, 0, 9));
                offset = new Vector2(HashSigned(i, step, 1) * s.axes.x,
                                     HashSigned(i, step, 2) * s.axes.y) * (amp * tremble);
                rotDeg = HashSigned(i, step, 3) * s.rotation * k * (0.4f + 0.6f * beat);
                scale = 1f + s.scalePulse * k * beat;
                break;
            }

            case DialogueEmphasisProfile.EmphasisMotion.Squirm:
            {
                // Wriggle: out-of-sync sway, shear and twist per glyph, plus a lump crawling along.
                float u = Tau * t - p;
                float seed = Hash01(i, 0, 21) * Tau;
                offset = new Vector2(Mathf.Sin(u * 1.7f + seed) * amp * s.axes.x,
                                     Mathf.Sin(u) * amp * s.axes.y);
                lean = Mathf.Sin(u * 1.3f + 0.5f) * s.shear * k;
                rotDeg = Mathf.Sin(u * 0.8f + 2f + seed) * s.rotation * k;

                float lump = Mathf.Max(0f, Mathf.Sin(Tau * t * 0.45f - i * 0.5f));
                lump *= lump; lump *= lump; lump *= lump * lump; // ^16-ish: tight travelling bulge
                scale = 1f + s.scalePulse * k * lump;
                offset.y += lump * amp * 0.8f;
                break;
            }
        }
    }
}
