using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One subject line on the paper Shift Report: index, ID photo, name + ID number, anomalies
/// caught / total, coupons earned, and a verdict icon. Killed subjects get a red X struck over
/// their photo.
///
/// Every animation is returned as a plain <see cref="IEnumerator"/> so it runs inside the
/// report's own coroutine — the row never owns a Coroutine the report has to wait on (see the
/// never-trap rules on <see cref="EndOfShiftReportUI"/>). All animation uses unscaled time.
/// </summary>
public class ShiftReportSubjectRow : MonoBehaviour
{
    [Serializable]
    public struct VerdictStyle
    {
        [Tooltip("Icon drawn in the verdict column. Leave empty to show only the stamp label.")]
        public Texture icon;
        public Color iconColor;
        [Tooltip("Optional stamp word next to the icon (e.g. QUARANTINE). Empty hides the label.")]
        public string stampLabel;
        [Tooltip("Row background tint. Alpha 0 leaves the paper showing through.")]
        public Color rowTint;
        [Tooltip("Ink color used for the anomaly count and earnings on this row.")]
        public Color valueColor;
    }

    [Header("Layout")]
    [Tooltip("Scaled during the pop-in. Should be a child that is not driven by a layout group.")]
    [SerializeField] private RectTransform animRoot;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image rowBackground;

    [Header("Subject")]
    [SerializeField] private TextMeshProUGUI indexText;
    [SerializeField] private RawImage photo;
    [Tooltip("Shown instead of the photo when the subject's ID photo can't be resolved.")]
    [SerializeField] private GameObject photoPlaceholder;
    [Tooltip("Red X struck over the photo for killed subjects.")]
    [SerializeField] private RectTransform killMark;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI idText;

    [Header("Values")]
    [SerializeField] private TextMeshProUGUI anomaliesText;
    [SerializeField] private TextMeshProUGUI anomaliesCaptionText;
    [SerializeField] private TextMeshProUGUI earningsText;

    [Header("Verdict")]
    [SerializeField] private RectTransform verdictRoot;
    [SerializeField] private RawImage verdictIcon;
    [SerializeField] private TextMeshProUGUI verdictStampText;

    [Header("Verdict Styles")]
    [SerializeField] private VerdictStyle passedStyle = new VerdictStyle
    {
        iconColor = new Color(0.22f, 0.33f, 0.2f, 1f),
        rowTint = new Color(0f, 0f, 0f, 0f),
        valueColor = new Color(0.14f, 0.12f, 0.1f, 1f)
    };
    [SerializeField] private VerdictStyle quarantinedStyle = new VerdictStyle
    {
        iconColor = new Color(0.62f, 0.13f, 0.1f, 1f),
        stampLabel = "QUARANTINE",
        rowTint = new Color(0.75f, 0.2f, 0.15f, 0.16f),
        valueColor = new Color(0.62f, 0.13f, 0.1f, 1f)
    };
    [SerializeField] private VerdictStyle killedStyle = new VerdictStyle
    {
        iconColor = new Color(0.62f, 0.13f, 0.1f, 1f),
        rowTint = new Color(0f, 0f, 0f, 0f),
        valueColor = new Color(0.62f, 0.13f, 0.1f, 1f)
    };
    [SerializeField] private VerdictStyle fledStyle = new VerdictStyle
    {
        iconColor = new Color(0.45f, 0.35f, 0.2f, 1f),
        stampLabel = "FLED",
        rowTint = new Color(0.5f, 0.4f, 0.2f, 0.12f),
        valueColor = new Color(0.45f, 0.35f, 0.2f, 1f)
    };

    [Header("Animation")]
    [SerializeField] private float popStartScale = 0.85f;
    [Tooltip("Overshoot strength of the pop (ease-out-back).")]
    [SerializeField] private float popOvershoot = 1.6f;
    [SerializeField] private float stampStartScale = 1.9f;

    private bool _isKilled;
    private bool _hasVerdictIcon;
    private bool _hasStampLabel;

    public bool IsKilled => _isKilled;

    private void Awake()
    {
        EnsureCanvasGroup();
    }

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private RectTransform AnimTarget => animRoot != null ? animRoot : (RectTransform)transform;

    /// <summary>Fills every field for <paramref name="subject"/> and leaves the row hidden, ready to pop in.</summary>
    public void Bind(int index, ShiftSubjectResult subject)
    {
        VerdictStyle style = StyleFor(subject.Verdict);
        _isKilled = subject.Verdict == ShiftSubjectVerdict.Killed;

        if (indexText != null)
            indexText.text = (index + 1).ToString("00");

        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(subject.SubjectName) ? "Unknown Subject" : subject.SubjectName;

        if (idText != null)
            idText.text = FormatId(subject.IDNumber);

        bool notAssessed = subject.AnomaliesCaught == ShiftSubjectResult.NotAssessed;
        if (anomaliesText != null)
        {
            anomaliesText.text = notAssessed ? "— / —" : $"{subject.AnomaliesCaught} / {subject.AnomaliesTotal}";
            anomaliesText.color = style.valueColor;
        }
        if (anomaliesCaptionText != null)
            anomaliesCaptionText.text = notAssessed ? "Not Assessed" : "Anomalies";

        if (earningsText != null)
        {
            earningsText.text = subject.CouponsEarned.ToString();
            earningsText.color = style.valueColor;
        }

        if (rowBackground != null)
        {
            rowBackground.color = style.rowTint;
            rowBackground.enabled = style.rowTint.a > 0.001f;
        }

        BindPhoto(subject.ResolveIDPhoto());

        _hasVerdictIcon = verdictIcon != null && style.icon != null;
        if (verdictIcon != null)
        {
            verdictIcon.texture = style.icon;
            verdictIcon.color = style.iconColor;
            verdictIcon.gameObject.SetActive(_hasVerdictIcon);
        }

        _hasStampLabel = verdictStampText != null && !string.IsNullOrEmpty(style.stampLabel);
        if (verdictStampText != null)
        {
            verdictStampText.text = style.stampLabel ?? string.Empty;
            verdictStampText.color = style.iconColor;
            verdictStampText.gameObject.SetActive(_hasStampLabel);
        }

        SetVerdictProgress(0f);
        SetKillMarkProgress(0f);
        SetPop(0f);
    }

    private void BindPhoto(Texture2D texture)
    {
        bool hasPhoto = texture != null;

        if (photo != null)
        {
            photo.texture = texture;
            photo.enabled = hasPhoto;
            if (hasPhoto)
                photo.uvRect = CoverUv(texture, photo.rectTransform.rect.size);
        }

        if (photoPlaceholder != null)
            photoPlaceholder.SetActive(!hasPhoto);
    }

    /// <summary>Center-crops the texture so it fills the photo box without stretching.</summary>
    private static Rect CoverUv(Texture texture, Vector2 boxSize)
    {
        if (texture == null || boxSize.x <= 0f || boxSize.y <= 0f || texture.height == 0)
            return new Rect(0f, 0f, 1f, 1f);

        float texAspect = (float)texture.width / texture.height;
        float boxAspect = boxSize.x / boxSize.y;

        if (texAspect > boxAspect)
        {
            float w = boxAspect / texAspect;
            return new Rect((1f - w) * 0.5f, 0f, w, 1f);
        }

        float h = texAspect / boxAspect;
        return new Rect(0f, (1f - h) * 0.5f, 1f, h);
    }

    private static string FormatId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "ID ----";

        return $"ID {id}";
    }

    private VerdictStyle StyleFor(ShiftSubjectVerdict verdict)
    {
        switch (verdict)
        {
            case ShiftSubjectVerdict.Quarantined: return quarantinedStyle;
            case ShiftSubjectVerdict.Killed: return killedStyle;
            case ShiftSubjectVerdict.Fled: return fledStyle;
            default: return passedStyle;
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    /// <summary>Jumps straight to the fully revealed state (used by skip / failsafe paths).</summary>
    public void SnapFinal()
    {
        Show();
        SetPop(1f);
        SetVerdictProgress(1f);
        SetKillMarkProgress(_isKilled ? 1f : 0f);
    }

    // ----- Animation (caller-driven) -----

    public IEnumerator PopIn(float duration)
    {
        yield return Animate(duration, SetPop);
    }

    public IEnumerator StampVerdict(float duration)
    {
        yield return Animate(duration, SetVerdictProgress);
    }

    public IEnumerator StrikePhoto(float duration)
    {
        if (!_isKilled)
            yield break;

        yield return Animate(duration, SetKillMarkProgress);
    }

    private static IEnumerator Animate(float duration, Action<float> apply)
    {
        if (duration <= 0f)
        {
            apply(1f);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            apply(Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        apply(1f);
    }

    private void SetPop(float t)
    {
        EnsureCanvasGroup();
        canvasGroup.alpha = Mathf.Clamp01(t * 2.5f);
        float scale = Mathf.LerpUnclamped(popStartScale, 1f, EaseOutBack(t, popOvershoot));
        AnimTarget.localScale = new Vector3(scale, scale, 1f);
    }

    private void SetVerdictProgress(float t)
    {
        if (verdictRoot == null)
            return;

        bool visible = t > 0f && (_hasVerdictIcon || _hasStampLabel);
        verdictRoot.gameObject.SetActive(visible);
        if (!visible)
            return;

        // A heavy stamp: starts large and faint, slams down, with a small settle.
        float eased = EaseOutBack(t, 1.2f);
        float scale = Mathf.LerpUnclamped(stampStartScale, 1f, eased);
        verdictRoot.localScale = new Vector3(scale, scale, 1f);

        float alpha = Mathf.Clamp01(t * 3f);
        if (verdictIcon != null)
        {
            Color c = verdictIcon.color;
            c.a = alpha;
            verdictIcon.color = c;
        }
        if (verdictStampText != null)
            verdictStampText.alpha = alpha;
    }

    private void SetKillMarkProgress(float t)
    {
        if (killMark == null)
            return;

        killMark.gameObject.SetActive(t > 0f);
        float scale = Mathf.LerpUnclamped(1.6f, 1f, EaseOutBack(t, 1.4f));
        killMark.localScale = new Vector3(scale, scale, 1f);
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        t = Mathf.Clamp01(t);
        float c3 = overshoot + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + overshoot * p * p;
    }
}
