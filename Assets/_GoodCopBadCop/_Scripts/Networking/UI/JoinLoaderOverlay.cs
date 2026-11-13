using Netcode.Transports.Facepunch;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace GoodCopBadCop.Networking
{
    /// <summary>
    /// Full-screen loader shown on a joining client from <c>StartClient</c> until the host's late-join
    /// bootstrap hands control back to the game (see <see cref="Complete"/> call sites in GameManager).
    /// A Day 2+ session sends the joiner several MB of synchronization data, which the Steam transport
    /// streams in chunks; this overlay reports that progress instead of leaving the main menu frozen.
    ///
    /// Self-instantiating and built entirely in code (no scene/prefab dependency), mirroring the
    /// pattern used by <see cref="CheckpointIntegrityService"/>.
    ///
    /// Dismissed by: <see cref="Complete"/> (fade out), <see cref="Cancel"/> (immediate), the local
    /// client stopping/disconnecting, or a safety timeout so a missed completion signal can never
    /// leave the screen covered.
    /// </summary>
    public class JoinLoaderOverlay : MonoBehaviour
    {
        private const int SortingOrder = 32000;
        private const float FadeInDuration = 0.2f;
        private const float FadeOutDuration = 0.35f;
        private const float BarFillSpeed = 1.5f;

        /// <summary>Hide anyway this long after the local client is fully connected and has a player object.</summary>
        private const float ConnectedFallbackSeconds = 25f;

        /// <summary>Absolute upper bound on how long the overlay can stay up.</summary>
        private const float HardTimeoutSeconds = 240f;

        private static JoinLoaderOverlay _instance;

        private CanvasGroup _group;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _detail;
        private RectTransform _barFill;

        private bool _visible;
        private bool _hiding;
        private float _shownAt;
        private float _connectedAt = -1f;
        private float _displayedProgress;
        private NetworkManager _subscribedManager;

        /// <summary>True while the overlay is covering the screen.</summary>
        public static bool IsVisible => _instance != null && _instance._visible;

        /// <summary>Shows the loader. Call immediately before <c>NetworkManager.StartClient</c>.</summary>
        public static void Show()
        {
            GetOrCreate().BeginShow();
        }

        /// <summary>The join finished and the game's own transition has taken over — fade the loader out.</summary>
        public static void Complete()
        {
            if (_instance != null && _instance._visible)
                _instance._hiding = true;
        }

        /// <summary>The join was aborted (failed to start, disconnected) — hide immediately.</summary>
        public static void Cancel()
        {
            if (_instance != null)
                _instance.HideImmediate();
        }

        private static JoinLoaderOverlay GetOrCreate()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("[JoinLoaderOverlay]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<JoinLoaderOverlay>();
            _instance.BuildUi();
            return _instance;
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            Unsubscribe();
            if (_instance == this)
                _instance = null;
        }

        private void BeginShow()
        {
            _visible = true;
            _hiding = false;
            _shownAt = Time.unscaledTime;
            _connectedAt = -1f;
            _displayedProgress = 0f;
            _group.alpha = 0f;
            _group.blocksRaycasts = true;
            gameObject.SetActive(true);

            RefreshFont();
            SetStatus(Loc.T("Connecting to host..."), string.Empty);
            SetBar(0f, 0f);
            Subscribe();
        }

        private void HideImmediate()
        {
            _visible = false;
            _hiding = false;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            Unsubscribe();
            gameObject.SetActive(false);
        }

        private void Subscribe()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm == _subscribedManager)
                return;

            Unsubscribe();
            _subscribedManager = nm;
            nm.OnClientStopped += HandleClientStopped;
        }

        private void Unsubscribe()
        {
            if (_subscribedManager != null)
                _subscribedManager.OnClientStopped -= HandleClientStopped;
            _subscribedManager = null;
        }

        private void HandleClientStopped(bool wasHost)
        {
            HideImmediate();
        }

        private void Update()
        {
            if (!_visible)
                return;

            float dt = Time.unscaledDeltaTime;

            if (_hiding)
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, dt / FadeOutDuration);
                if (_group.alpha <= 0f)
                    HideImmediate();
                return;
            }

            _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt / FadeInDuration);

            float elapsed = Time.unscaledTime - _shownAt;
            if (elapsed > HardTimeoutSeconds)
            {
                Debug.LogWarning($"[JoinLoaderOverlay] Still visible after {HardTimeoutSeconds}s — hiding as a safety net.");
                _hiding = true;
                return;
            }

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null)
                return;

            if (nm.IsConnectedClient)
            {
                if (_connectedAt < 0f)
                    _connectedAt = Time.unscaledTime;

                SetStatus(Loc.T("Preparing the checkpoint..."), string.Empty);
                SetBar(1f, 0f);

                bool hasPlayer = nm.LocalClient != null && nm.LocalClient.PlayerObject != null;
                if (hasPlayer && Time.unscaledTime - _connectedAt > ConnectedFallbackSeconds)
                {
                    Debug.LogWarning("[JoinLoaderOverlay] Join completion signal never arrived — hiding after fallback delay.");
                    _hiding = true;
                }
                return;
            }

            var steam = nm.NetworkConfig?.NetworkTransport as FacepunchTransport;
            if (steam == null)
            {
                // LAN/UnityTransport: no byte-level progress available.
                SetStatus(Loc.T("Synchronizing with host..."), string.Empty);
                SetIndeterminate();
                return;
            }

            if (!steam.IsClientTransportConnected)
            {
                SetStatus(Loc.T("Connecting to host..."), string.Empty);
                SetIndeterminate();
                return;
            }

            if (steam.TryGetIncomingTransferProgress(out int received, out int total) && total > 0)
            {
                float progress = (float)received / total;
                SetStatus(Loc.T("Downloading session data..."), Loc.T("{0} / {1} MB", ToMb(received), ToMb(total)));
                SetBar(progress, 0f);
                return;
            }

            SetStatus(Loc.T("Synchronizing with host..."),
                steam.ClientBytesReceived > 0 ? Loc.T("{0} MB received", ToMb(steam.ClientBytesReceived)) : string.Empty);
            SetIndeterminate();
        }

        private static string ToMb(long bytes) => (bytes / (1024f * 1024f)).ToString("0.0");

        // ── Presentation ─────────────────────────────────────────────────────────

        private void SetStatus(string status, string detail)
        {
            if (_status.text != status) _status.text = status;
            if (_detail.text != detail) _detail.text = detail;
        }

        /// <summary>Determinate fill from the left edge, smoothed so chunk arrivals don't jump.</summary>
        private void SetBar(float progress, float start)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            _displayedProgress = Mathf.MoveTowards(_displayedProgress, Mathf.Clamp01(progress), BarFillSpeed * dt);
            _barFill.anchorMin = new Vector2(start, 0f);
            _barFill.anchorMax = new Vector2(Mathf.Max(start, _displayedProgress), 1f);
        }

        /// <summary>A short segment sweeping across the track while the total size is unknown.</summary>
        private void SetIndeterminate()
        {
            const float Segment = 0.25f;
            float t = Mathf.Repeat(Time.unscaledTime * 0.6f, 1f + Segment) - Segment;
            _barFill.anchorMin = new Vector2(Mathf.Clamp01(t), 0f);
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(t + Segment), 1f);
            _displayedProgress = 0f;
        }

        private void RefreshFont()
        {
            TMP_FontAsset font = LocalizationSettings.Localize(TMP_Settings.defaultFontAsset);
            if (font == null) return;
            _status.font = font;
            _detail.font = font;
        }

        private void BuildUi()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            _group = gameObject.AddComponent<CanvasGroup>();

            var background = CreateRect("Background", transform);
            Stretch(background);
            background.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.94f);

            _status = CreateText("Status", transform, 44f, new Color(0.92f, 0.9f, 0.84f, 1f));
            Place(_status.rectTransform, new Vector2(0f, 40f), new Vector2(1200f, 70f));

            var track = CreateRect("BarTrack", transform);
            Place(track, new Vector2(0f, -20f), new Vector2(640f, 10f));
            track.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);

            _barFill = CreateRect("BarFill", track);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            _barFill.gameObject.AddComponent<Image>().color = new Color(0.92f, 0.9f, 0.84f, 1f);

            _detail = CreateText("Detail", transform, 26f, new Color(0.92f, 0.9f, 0.84f, 0.6f));
            Place(_detail.rectTransform, new Vector2(0f, -65f), new Vector2(1200f, 50f));

            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float size, Color color)
        {
            var text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
