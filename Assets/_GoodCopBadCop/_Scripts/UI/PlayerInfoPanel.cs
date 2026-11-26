using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Displays a single player's name, ready state, and host status in the pre-game lobby screen.
/// </summary>
public class PlayerInfoPanel : MonoBehaviour
{
    private static readonly Color ReadyColor    = new Color(0.18f, 0.72f, 0.18f, 1f);
    private static readonly Color NotReadyColor = new Color(0.85f, 0.15f, 0.15f, 1f);

    private const string ReadyText    = "READY";
    private const string NotReadyText = "NOT READY";

    [SerializeField] private TextMeshProUGUI userNameText;

    /// <summary>TMP label that shows READY / NOT READY with matching colour.</summary>
    [SerializeField] private TextMeshProUGUI readyIndicator;

    /// <summary>Crown icon shown only for the lobby host.</summary>
    [SerializeField] private GameObject hostIcon;

    [Header("Kick (host only)")]
    [Tooltip("Optional host-only kick button. Hidden unless SetKickAction is given a callback.")]
    [SerializeField] private Button kickButton;
    [SerializeField] private TMP_Text kickButtonLabel;
    [Tooltip("Seconds the button waits for the confirming second click.")]
    [SerializeField] private float kickConfirmWindow = 3f;
    [Tooltip("Gap between the kick button and the status label it pushes left.")]
    [SerializeField] private float kickButtonSpacing = 12f;

    // English source text; render-time localization translates code-set labels.
    private static readonly string KickText = Loc.N("KICK");
    private static readonly string KickConfirmText = Loc.N("SURE?");

    private UnityAction _onKick;
    private float _kickArmedUntil = -1f;
    private RectTransform _readyRect;
    private Vector2 _readyBasePosition;
    private bool _readyBaseCached;

    private void Awake()
    {
        if (kickButton != null)
            kickButton.onClick.AddListener(OnKickClicked);
    }

    private void OnDisable() => DisarmKick();

    private void Update()
    {
        if (_kickArmedUntil > 0f && Time.unscaledTime > _kickArmedUntil)
            DisarmKick();
    }

    /// <summary>
    /// Shows the kick button with <paramref name="onKick"/> (two clicks: KICK, then SURE?), or hides
    /// it when null. The status label shifts left to make room.
    /// </summary>
    public void SetKickAction(UnityAction onKick)
    {
        _onKick = onKick;
        DisarmKick();

        bool show = onKick != null && kickButton != null;
        if (kickButton != null && kickButton.gameObject.activeSelf != show)
            kickButton.gameObject.SetActive(show);

        CacheReadyBase();
        if (_readyRect != null)
        {
            float offset = show ? ((RectTransform)kickButton.transform).rect.width + kickButtonSpacing : 0f;
            _readyRect.anchoredPosition = _readyBasePosition - new Vector2(offset, 0f);
        }
    }

    /// <summary>Replaces the READY / NOT READY label with free text (e.g. HOST / YOU in the pause menu).</summary>
    public void SetStatus(string text, Color color)
    {
        if (readyIndicator == null) return;
        readyIndicator.text = text;
        readyIndicator.color = color;
    }

    private void CacheReadyBase()
    {
        if (_readyBaseCached || readyIndicator == null) return;
        _readyRect = readyIndicator.rectTransform;
        _readyBasePosition = _readyRect.anchoredPosition;
        _readyBaseCached = true;
    }

    private void OnKickClicked()
    {
        if (_onKick == null) return;

        if (_kickArmedUntil < 0f)
        {
            _kickArmedUntil = Time.unscaledTime + kickConfirmWindow;
            if (kickButtonLabel != null) kickButtonLabel.text = KickConfirmText;
            SetKickVisualArmed(true);
            return;
        }

        UnityAction kick = _onKick;
        DisarmKick();
        kick.Invoke();
    }

    private void DisarmKick()
    {
        _kickArmedUntil = -1f;
        if (kickButtonLabel != null) kickButtonLabel.text = KickText;
        SetKickVisualArmed(false);
    }

    private void SetKickVisualArmed(bool armed)
    {
        if (kickButton != null && kickButton.TryGetComponent(out KickButtonVisual visual))
            visual.SetArmed(armed);
    }

    /// <summary>Populates the panel with a player name, optional ready state, and optional host flag.</summary>
    public void PopulateInfo(string userName, bool isReady = false, bool isHost = false)
    {
        userNameText.text = userName;
        SetReady(isReady);
        SetHost(isHost);
    }

    /// <summary>
    /// Turns this panel into a clickable empty-seat row ("Invite a Friend" / "Open Slot") for the
    /// pre-game lobby. Panels are destroyed and re-spawned on every lobby refresh, so this never
    /// needs to be reverted back to a player row.
    /// </summary>
    public void PopulateInviteSlot(string label, string slotText, UnityAction onClick)
    {
        userNameText.text = label;
        userNameText.color = InviteLabelColor;
        SetHost(false);
        SetKickAction(null);

        if (readyIndicator != null)
        {
            readyIndicator.text = slotText;
            readyIndicator.color = OpenSlotColor;
        }

        // The background panel is the only raycast-target Image; use it as the button graphic.
        Image background = GetComponentInChildren<Image>(true);
        if (background != null) background.color *= InviteBackgroundTint;

        MakeClickable(background, onClick);
    }

    private Button MakeClickable(Image background, UnityAction onClick)
    {
        if (!TryGetComponent(out Button button))
            button = gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.ColorTint;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(onClick);
        return button;
    }

    private static readonly Color InviteLabelColor     = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color OpenSlotColor        = new Color(0.25f, 0.25f, 0.25f, 0.8f);
    private static readonly Color InviteBackgroundTint = new Color(1f, 1f, 1f, 0.6f);

    /// <summary>Updates the ready indicator without changing the player name or host icon.</summary>
    public void SetReady(bool isReady)
    {
        if (readyIndicator == null) return;

        readyIndicator.text  = isReady ? ReadyText : NotReadyText;
        readyIndicator.color = isReady ? ReadyColor : NotReadyColor;
    }

    /// <summary>Shows or hides the host crown icon.
    /// Activation is deferred by one end-of-frame so TMPWidthFitter can finish
    /// resizing the player name before the layout reflows for the icon.</summary>
    public void SetHost(bool isHost)
    {
        if (hostIcon == null) return;

        if (!isHost)
        {
            hostIcon.SetActive(false);
            return;
        }

        StartCoroutine(ActivateHostIconDelayed());
    }

    private IEnumerator ActivateHostIconDelayed()
    {
        yield return new WaitForEndOfFrame();
        if (hostIcon != null)
            hostIcon.SetActive(true);
    }
}
