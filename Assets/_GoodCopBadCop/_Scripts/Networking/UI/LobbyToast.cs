using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.Networking
{
    /// <summary>
    /// Short lobby/session notifications ("Joined X's lobby", "Couldn't join: lobby full", ...) shown at
    /// the top of the screen. Works on the main menu and in gameplay alike, because the HUD notification
    /// stacks are hidden while the menu is up.
    ///
    /// Self-instantiating and built entirely in code (no scene/prefab dependency), like
    /// <see cref="JoinLoaderOverlay"/>. Pass text that is already translated (Loc.T) or an English
    /// source (Loc.N); render-time localization translates the latter.
    /// </summary>
    public class LobbyToast : MonoBehaviour
    {
        public enum Kind { Info, Success, Error }

        private const int SortingOrder = 32010; // above the join loader so a result can show over it
        private const int MaxVisible = 3;
        private const float FadeInDuration = 0.2f;
        private const float FadeOutDuration = 0.4f;
        private const float InfoHoldSeconds = 4f;
        private const float ErrorHoldSeconds = 5.5f;
        private const float EntryHeight = 64f;
        private const float EntrySpacing = 10f;
        private const float TopOffset = 40f;

        private static readonly Color Background = new Color(0.06f, 0.05f, 0.04f, 0.92f);
        private static readonly Color TextColor = new Color(0.93f, 0.89f, 0.8f, 1f);
        private static readonly Color InfoAccent = new Color(0.93f, 0.89f, 0.8f, 1f);
        private static readonly Color SuccessAccent = new Color(0.45f, 0.75f, 0.35f, 1f);
        private static readonly Color ErrorAccent = new Color(0.9f, 0.38f, 0.2f, 1f);

        private class Entry
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public TextMeshProUGUI Text;
            public string Message;
            public float ShownAt;
            public float Hold;
            public bool Hiding;
        }

        private static LobbyToast _instance;
        private readonly List<Entry> _entries = new List<Entry>();
        private RectTransform _root;

        /// <summary>Shows a notification. A message identical to one already on screen restarts its timer instead of stacking.</summary>
        public static void Show(string message, Kind kind = Kind.Info)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            GetOrCreate().Add(message, kind);
        }

        public static void Error(string message) => Show(message, Kind.Error);
        public static void Success(string message) => Show(message, Kind.Success);

        private static LobbyToast GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("[LobbyToast]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LobbyToast>();
            _instance.BuildUi();
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Add(string message, Kind kind)
        {
            float hold = kind == Kind.Error ? ErrorHoldSeconds : InfoHoldSeconds;

            foreach (Entry existing in _entries)
            {
                if (existing.Message != message) continue;
                existing.ShownAt = Time.unscaledTime;
                existing.Hold = hold;
                existing.Hiding = false;
                return;
            }

            while (_entries.Count >= MaxVisible)
                Remove(_entries[0]);

            _entries.Add(CreateEntry(message, kind, hold));
            Layout();
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];
                if (!e.Hiding && now - e.ShownAt > e.Hold)
                    e.Hiding = true;

                if (e.Hiding)
                {
                    e.Group.alpha = Mathf.MoveTowards(e.Group.alpha, 0f, dt / FadeOutDuration);
                    if (e.Group.alpha <= 0f)
                    {
                        Remove(e);
                        Layout();
                    }
                }
                else
                {
                    e.Group.alpha = Mathf.MoveTowards(e.Group.alpha, 1f, dt / FadeInDuration);
                }
            }
        }

        private void Remove(Entry entry)
        {
            _entries.Remove(entry);
            if (entry.Rect != null)
                Destroy(entry.Rect.gameObject);
        }

        private void Layout()
        {
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Rect.anchoredPosition = new Vector2(0f, -TopOffset - i * (EntryHeight + EntrySpacing));
        }

        // ── Presentation ─────────────────────────────────────────────────────────

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _root = (RectTransform)transform;
        }

        private Entry CreateEntry(string message, Kind kind, float hold)
        {
            var rect = CreateRect("Toast", _root);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(900f, EntryHeight);

            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var bg = rect.gameObject.AddComponent<Image>();
            bg.color = Background;
            bg.raycastTarget = false;

            var accent = CreateRect("Accent", rect);
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.anchoredPosition = Vector2.zero;
            accent.sizeDelta = new Vector2(8f, 0f);
            var accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = kind == Kind.Error ? ErrorAccent : kind == Kind.Success ? SuccessAccent : InfoAccent;
            accentImage.raycastTarget = false;

            var textRect = CreateRect("Text", rect);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(32f, 6f);
            textRect.offsetMax = new Vector2(-24f, -6f);

            var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset font = LocalizationSettings.Localize(TMP_Settings.defaultFontAsset);
            if (font != null) text.font = font;
            text.fontSize = 30f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18f;
            text.fontSizeMax = 30f;
            text.color = TextColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.text = message;

            return new Entry
            {
                Rect = rect,
                Group = group,
                Text = text,
                Message = message,
                ShownAt = Time.unscaledTime,
                Hold = hold,
            };
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
