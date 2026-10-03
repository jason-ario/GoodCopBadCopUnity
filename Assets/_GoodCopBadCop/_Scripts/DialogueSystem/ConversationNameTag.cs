using TMPro;
using UnityEngine;

/// <summary>
/// Single screen-top name tag showing who the local player is in a conversation with.
/// Independent of individual subtitles: it fades in once when the local player enters a
/// conversation (<see cref="OverhearRange.IsLocalPlayerConversationParticipant"/>), stays up for
/// the whole conversation (lines, gaps, choices), and fades out when the conversation ends.
/// Speaker changes mid-conversation swap the text in place without re-fading.
/// Lives on the dialogue root canvas so the pause-menu CanvasGroup still hides it.
/// </summary>
public class ConversationNameTag : MonoBehaviour
{
    [Tooltip("Subtitle prefab whose (hidden) Name Tag child is cloned as this tag's visuals.")]
    [SerializeField] private Subtitles template;

    [Tooltip("Distance in canvas units from the top edge of the screen to the top of the tag.")]
    [SerializeField] private float topOffset = 40f;

    [Tooltip("Show only the first word of the speaker name (e.g. 'Vlad' from 'Vlad Petrov').")]
    [SerializeField] private bool firstNameOnly = true;

    [Tooltip("Label color used when the speaker color is white/unset.")]
    [SerializeField] private Color defaultNameColor = new Color(1f, 0.8f, 0.35f, 1f);

    [SerializeField] private float fadeInDuration = 0.2f;
    [SerializeField] private float fadeOutDuration = 0.2f;

    [Tooltip("An NPC line reported this many seconds before the conversation starts is used as the partner name " +
             "(covers the first line arriving just before the dialogue-mode flag is set).")]
    [SerializeField] private float preEntryLineWindow = 1f;

    private static ConversationNameTag s_instance;

    private CanvasGroup _tag;
    private TMP_Text _label;
    private bool _inConversation;
    private string _shownName;
    private float _targetAlpha;

    private string _lastReportedName;
    private Color _lastReportedColor;
    private float _lastReportTime = float.NegativeInfinity;

    /// <summary>Called for every NPC subtitle line. Updates the tag while in a conversation.</summary>
    public static void ReportSpeaker(string speakerName, Color nameColor)
    {
        if (s_instance != null)
            s_instance.OnSpeakerReported(speakerName, nameColor);
    }

    private void Awake()
    {
        s_instance = this;
        BuildTag();
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        if (_tag != null) Destroy(_tag.gameObject);
    }

    private void BuildTag()
    {
        if (template == null || template.NameTagTemplate == null)
        {
            Debug.LogWarning("ConversationNameTag: no subtitle template with a Name Tag assigned.", this);
            return;
        }

        var canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.rootCanvas.transform : transform;

        GameObject go = Instantiate(template.NameTagTemplate.gameObject, parent, false);
        go.name = "Conversation Name Tag";

        _tag = go.GetComponent<CanvasGroup>();
        _label = go.GetComponentInChildren<TMP_Text>(true);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -topOffset);
        rect.SetAsLastSibling();

        _tag.alpha = 0f;
        go.SetActive(false);
    }

    private void Update()
    {
        if (_tag == null) return;

        bool inConversation = OverhearRange.IsLocalPlayerConversationParticipant;

        if (inConversation && !_inConversation)
        {
            _inConversation = true;
            _shownName = null;
            if (Time.unscaledTime - _lastReportTime <= preEntryLineWindow)
                Apply(_lastReportedName, _lastReportedColor);
        }
        else if (!inConversation && _inConversation)
        {
            _inConversation = false;
            _shownName = null;
            _targetAlpha = 0f;
        }

        if (_inConversation && _shownName == null)
            TrySeedFromPartner();

        AnimateAlpha();
    }

    private void OnSpeakerReported(string speakerName, Color nameColor)
    {
        _lastReportedName = speakerName;
        _lastReportedColor = nameColor;
        _lastReportTime = Time.unscaledTime;

        if (_inConversation)
            Apply(speakerName, nameColor);
    }

    /// <summary>Shows the partner's name before their first line, when it can be resolved.</summary>
    private void TrySeedFromPartner()
    {
        if (OverhearRange.TryGetLocalConversationPartner(out SpeakingInteraction speaking))
            Apply(speaking.SpeakerName, Color.white);
    }

    private void Apply(string rawName, Color nameColor)
    {
        if (_tag == null || _label == null) return;

        string display = FormatName(rawName);
        if (string.IsNullOrEmpty(display)) return;

        _label.text = display;
        bool useSpeakerColor = nameColor.a > 0f && nameColor != Color.white;
        _label.color = useSpeakerColor ? nameColor : defaultNameColor;

        if (_shownName == null)
        {
            _tag.gameObject.SetActive(true);
            _targetAlpha = 1f;
        }
        _shownName = display;
    }

    private void AnimateAlpha()
    {
        if (!_tag.gameObject.activeSelf) return;

        float duration = _targetAlpha > _tag.alpha ? fadeInDuration : fadeOutDuration;
        _tag.alpha = duration > 0f
            ? Mathf.MoveTowards(_tag.alpha, _targetAlpha, Time.unscaledDeltaTime / duration)
            : _targetAlpha;

        if (_targetAlpha <= 0f && _tag.alpha <= 0f)
            _tag.gameObject.SetActive(false);
    }

    private string FormatName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return null;
        string trimmed = rawName.Trim();
        if (!firstNameOnly) return trimmed;
        int space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed.Substring(0, space) : trimmed;
    }
}
