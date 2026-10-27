using System.Collections.Generic;
using GoodCopBadCop.Input;
using UnityEngine;

/// <summary>
/// Shared on-screen control hints ("{0} to check / uncheck", "Hold {0} to rotate"; {0} = icon, see
/// <see cref="IconPrompt"/>) for modes that hide the
/// Player HUD: diegetic views (lever, bunker door wheel, ...), the exam page draw mode, and anything new.
///
/// Usage: <c>ControlHintsOverlay.Show(this, hints)</c> on enter, <c>ControlHintsOverlay.Hide(this)</c> on exit.
/// Requests are keyed by owner. The most recent request is shown, and hiding it reveals the one underneath.
/// A destroyed Unity owner is dropped automatically. Requests made before the overlay exists are kept and
/// shown once it does.
///
/// Rows are pooled instances of <see cref="_rowPrefab"/> (the <c>Control Hint Row</c> prefab) under
/// <see cref="_rowContainer"/>. Icons follow the active device and rebinding. Hidden while paused.
///
/// Attach to an always-active object outside the Player HUD. Only <see cref="_root"/> is toggled.
/// </summary>
public class ControlHintsOverlay : MonoBehaviour
{
    [Tooltip("Visual root toggled while a request is shown. Must be a child of this object.")]
    [SerializeField] private GameObject _root;

    [Tooltip("Parent the rows are spawned under (normally has a VerticalLayoutGroup).")]
    [SerializeField] private Transform _rowContainer;

    [Tooltip("Control Hint Row prefab.")]
    [SerializeField] private ControlHintRow _rowPrefab;

    [Tooltip("Shared icon set used for action / control-path lookups.")]
    [SerializeField] private InputIconDatabase _iconDatabase;

    private struct Request
    {
        public object owner;
        public IReadOnlyList<ControlHint> hints;
    }

    private static readonly List<Request> _requests = new List<Request>();
    private static ControlHintsOverlay _instance;
    private static bool _dirty;

    private readonly List<ControlHintRow> _rows = new List<ControlHintRow>();

    // Keeps every pooled row's labels at one shared size so rows never differ in height.
    private TextSizeGroup _sizeGroup;

    /// <summary>
    /// Shows <paramref name="hints"/> for <paramref name="owner"/>, replacing any previous request from the
    /// same owner and moving it to the top. The list is read again on every refresh, so keep it unchanged
    /// while shown, or call Show again after changing it.
    /// </summary>
    public static void Show(object owner, IReadOnlyList<ControlHint> hints)
    {
        if (owner == null) return;
        RemoveOwner(owner);
        if (hints != null && hints.Count > 0)
            _requests.Add(new Request { owner = owner, hints = hints });
        _dirty = true;
    }

    /// <summary>Convenience overload for a single row.</summary>
    public static void Show(object owner, ControlHint hint) => Show(owner, new[] { hint });

    /// <summary>Removes <paramref name="owner"/>'s request. Safe to call when it has none.</summary>
    public static void Hide(object owner)
    {
        if (owner == null) return;
        if (RemoveOwner(owner)) _dirty = true;
    }

    /// <summary>True while <paramref name="owner"/> has a request (shown or covered).</summary>
    public static bool IsShowing(object owner)
    {
        foreach (Request r in _requests)
            if (ReferenceEquals(r.owner, owner)) return true;
        return false;
    }

    private static bool RemoveOwner(object owner)
    {
        int removed = _requests.RemoveAll(r => ReferenceEquals(r.owner, owner));
        return removed > 0;
    }

    // Domain reload is disabled for Play Mode, so statics survive between sessions — reset them.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _requests.Clear();
        _instance = null;
        _dirty = false;
    }

    private void Awake()
    {
        _instance = this;
        SetActive(_root, false);

        if (_rowContainer != null)
        {
            if (!_rowContainer.TryGetComponent(out _sizeGroup)) _sizeGroup = _rowContainer.gameObject.AddComponent<TextSizeGroup>();
            _sizeGroup.measureInactive = true;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void OnEnable()
    {
        ActiveInputDeviceTracker.EnsureSubscribed();
        ActiveInputDeviceTracker.DeviceChanged += OnDeviceChanged;
        RebindableInput.BindingChanged += OnBindingChanged;
        Loc.LanguageChanged += OnLanguageChanged;
        _dirty = true;
    }

    private void OnDisable()
    {
        ActiveInputDeviceTracker.DeviceChanged -= OnDeviceChanged;
        RebindableInput.BindingChanged -= OnBindingChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        SetActive(_root, false);
    }

    private void OnDeviceChanged(bool isGamepad) => _dirty = true;

    private void OnBindingChanged(GameAction action) => _dirty = true;

    // Row text is localized when built, so a language switch needs a rebuild.
    private static void OnLanguageChanged() => _dirty = true;

    private void Update()
    {
        // Owners destroyed without calling Hide (scene reload, despawn) must not leave rows behind.
        if (_requests.RemoveAll(r => r.owner is Object o && o == null) > 0) _dirty = true;

        bool paused = UIController.Instance != null && UIController.Instance.IsPaused;
        bool visible = !paused && _requests.Count > 0;
        SetActive(_root, visible);

        if (visible && _dirty) Rebuild(_requests[_requests.Count - 1].hints);
    }

    private void Rebuild(IReadOnlyList<ControlHint> hints)
    {
        _dirty = false;
        bool gamepad = ActiveInputDeviceTracker.IsGamepad;

        int used = 0;
        foreach (ControlHint hint in hints)
        {
            if (hint == null || !hint.IsShownFor(gamepad)) continue;

            ControlHintRow row = GetRow(used++);
            if (row == null) break;
            row.Apply(IconPrompt.Localize(hint.TextFor(gamepad)), hint.ResolveIcon(gamepad, _iconDatabase));
            SetActive(row.gameObject, true);
        }

        for (int i = used; i < _rows.Count; i++)
        {
            _rows[i].Clear();
            SetActive(_rows[i].gameObject, false);
        }

        if (_sizeGroup != null) _sizeGroup.MarkDirty();
    }

    private ControlHintRow GetRow(int index)
    {
        while (_rows.Count <= index)
        {
            if (_rowPrefab == null || _rowContainer == null)
            {
                Debug.LogWarning("[ControlHintsOverlay] Row prefab or row container is not assigned.", this);
                return null;
            }
            ControlHintRow row = Instantiate(_rowPrefab, _rowContainer);
            _rows.Add(row);
            if (_sizeGroup != null)
            {
                Register(row.PrefixLabel);
                Register(row.Label);
            }
        }
        return _rows[index];
    }

    private void Register(TMPro.TMP_Text t)
    {
        if (t == null) return;
        float max = t.enableAutoSizing ? t.fontSizeMax : t.fontSize;
        float min = t.enableAutoSizing ? t.fontSizeMin : max;
        _sizeGroup.AddMember(t, max, min);
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }
}
