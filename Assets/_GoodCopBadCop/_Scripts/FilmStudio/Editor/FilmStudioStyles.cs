using System;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.FilmStudio
{
    /// <summary>
    /// Presentation-only style cache and drawing helpers for <see cref="FilmStudioWindow"/>.
    /// Styles and textures are built lazily inside OnGUI and rebuilt after a domain reload or an editor skin change.
    /// </summary>
    internal static class FilmStudioStyles
    {
        private const string LogoPath = "Assets/_GoodCopBadCop/FilmStudio/Editor/Branding/FilmStudioLogo.png";
        private const string LogoFallbackPath = "Assets/_GoodCopBadCop/_Textures/logo4.png";
        private const float HeaderHeight = 72f;

        public enum StatusLevel { Info, Success, Warning, Error }

        // ── Palette ───────────────────────────────────────────────────────────────
        public static readonly Color Accent = new(0.96f, 0.47f, 0.13f);
        private static readonly Color AccentHover = new(1f, 0.57f, 0.24f);
        private static readonly Color AccentActive = new(0.80f, 0.36f, 0.08f);
        public static readonly Color PoliceRed = new(0.86f, 0.15f, 0.19f);
        private static readonly Color PoliceRedHover = new(0.96f, 0.25f, 0.28f);
        private static readonly Color PoliceRedActive = new(0.70f, 0.10f, 0.13f);
        public static readonly Color PoliceBlue = new(0.17f, 0.42f, 0.95f);
        private static readonly Color HeaderBg = new(0.065f, 0.065f, 0.075f);
        private static readonly Color HeaderBand = new(0.105f, 0.105f, 0.12f);
        private static readonly Color Sprocket = new(0.23f, 0.23f, 0.25f);

        private static bool Pro => EditorGUIUtility.isProSkin;
        private static Color CardFill => Pro ? new Color(0.205f, 0.205f, 0.205f) : new Color(0.875f, 0.875f, 0.875f);
        private static Color CardBorder => Pro ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.64f, 0.64f, 0.64f);
        private static Color TabBarFill => Pro ? new Color(0.155f, 0.155f, 0.155f) : new Color(0.74f, 0.74f, 0.74f);
        private static Color TabHover => Pro ? new Color(0.185f, 0.185f, 0.185f) : new Color(0.80f, 0.80f, 0.80f);
        public static Color Divider => Pro ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.18f);
        private static Color StrongText => Pro ? new Color(0.94f, 0.94f, 0.94f) : new Color(0.08f, 0.08f, 0.08f);
        private static Color DimText => Pro ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.32f, 0.32f, 0.32f);
        public static Color RowHighlight => Pro ? new Color(0.96f, 0.47f, 0.13f, 0.10f) : new Color(0.96f, 0.47f, 0.13f, 0.16f);
        public static Color RowStripe => Pro ? new Color(1f, 1f, 1f, 0.025f) : new Color(0f, 0f, 0f, 0.035f);

        public static Color StatusColor(StatusLevel l) => l switch
        {
            StatusLevel.Success => new Color(0.30f, 0.76f, 0.42f),
            StatusLevel.Warning => new Color(0.97f, 0.72f, 0.18f),
            StatusLevel.Error => new Color(0.93f, 0.28f, 0.28f),
            _ => PoliceBlue,
        };

        // ── Cache ─────────────────────────────────────────────────────────────────
        private static bool _builtForPro;
        private static Texture2D _cardTex, _stripTex, _accentTex, _accentHoverTex, _accentActiveTex,
                                 _redTex, _redHoverTex, _redActiveTex, _bluePillTex, _darkPillTex;
        private static Texture2D _logo;
        private static bool _logoResolved;

        public static GUIStyle Card, Strip, CardTitle, CardHint, Primary, Record, Pill, BluePill, RedPill, DarkPill,
                               Wordmark, SubMark, TabLabel, TabLabelSelected, FilmTitle, Meta, StatusText,
                               BigTitle, Caption, Timecode;

        public static void Ensure()
        {
            if (Card != null && _cardTex != null && _builtForPro == Pro) return;
            Release();
            _builtForPro = Pro;

            _cardTex = MakeTex(CardFill, CardBorder);
            _stripTex = MakeTex(Pro ? new Color(0.175f, 0.175f, 0.175f) : new Color(0.82f, 0.82f, 0.82f), CardBorder);
            _accentTex = MakeTex(Accent, AccentActive);
            _accentHoverTex = MakeTex(AccentHover, AccentActive);
            _accentActiveTex = MakeTex(AccentActive, AccentActive);
            _redTex = MakeTex(PoliceRed, PoliceRedActive);
            _redHoverTex = MakeTex(PoliceRedHover, PoliceRedActive);
            _redActiveTex = MakeTex(PoliceRedActive, PoliceRedActive);
            _bluePillTex = MakeTex(PoliceBlue, null);
            _darkPillTex = MakeTex(new Color(0.15f, 0.15f, 0.17f), new Color(0.32f, 0.32f, 0.35f));

            Card = new GUIStyle
            {
                normal = { background = _cardTex },
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 6, 10),
                margin = new RectOffset(6, 6, 4, 6),
            };
            Strip = new GUIStyle(Card) { normal = { background = _stripTex }, padding = new RectOffset(10, 8, 6, 6), margin = new RectOffset(6, 6, 6, 2) };

            CardTitle = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft }, StrongText);
            CardHint = Colored(new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip }, DimText);

            Primary = MakeButton(_accentTex, _accentHoverTex, _accentActiveTex);
            Record = MakeButton(_redTex, _redHoverTex, _redActiveTex);

            Pill = MakePill(_accentTex, Color.white);
            BluePill = MakePill(_bluePillTex, Color.white);
            RedPill = MakePill(_redTex, Color.white);
            DarkPill = MakePill(_darkPillTex, new Color(0.85f, 0.85f, 0.88f));

            Wordmark = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip }, Color.white);
            SubMark = Colored(new GUIStyle(EditorStyles.miniBoldLabel) { fontSize = 9, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip }, Accent);

            TabLabel = Colored(new GUIStyle(EditorStyles.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip }, DimText);
            TabLabelSelected = Colored(new GUIStyle(TabLabel) { fontStyle = FontStyle.Bold }, StrongText);

            FilmTitle = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, clipping = TextClipping.Clip }, StrongText);
            Meta = Colored(new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip }, DimText);
            StatusText = Colored(new GUIStyle(EditorStyles.wordWrappedLabel) { padding = new RectOffset(12, 8, 5, 5), margin = new RectOffset(6, 6, 2, 4) }, StrongText);
            BigTitle = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = 16, wordWrap = true }, StrongText);
            Caption = Colored(new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontStyle = FontStyle.Italic }, Pro ? new Color(0.98f, 0.70f, 0.45f) : new Color(0.60f, 0.27f, 0.04f));
            Timecode = Colored(new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleRight }, StrongText);
        }

        private static void Release()
        {
            foreach (Texture2D t in new[] { _cardTex, _stripTex, _accentTex, _accentHoverTex, _accentActiveTex, _redTex, _redHoverTex, _redActiveTex, _bluePillTex, _darkPillTex })
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
        }

        private static Texture2D MakeTex(Color fill, Color? border)
        {
            int n = border.HasValue ? 3 : 1;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = border ?? fill;
            if (border.HasValue) px[4] = fill;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static GUIStyle Colored(GUIStyle s, Color c)
        {
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c;
            s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
            return s;
        }

        private static GUIStyle MakeButton(Texture2D normal, Texture2D hover, Texture2D active)
        {
            GUIStyle b = GUI.skin.button;
            var s = new GUIStyle
            {
                font = b.font,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1),
                padding = new RectOffset(10, 10, 4, 4),
                margin = b.margin,
                clipping = TextClipping.Clip,
                normal = { background = normal, textColor = Color.white },
                hover = { background = hover, textColor = Color.white },
                active = { background = active, textColor = Color.white },
                focused = { background = normal, textColor = Color.white },
            };
            return s;
        }

        private static GUIStyle MakePill(Texture2D bg, Color text) => Colored(new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { background = bg },
            border = new RectOffset(1, 1, 1, 1),
            padding = new RectOffset(6, 6, 1, 2),
            margin = new RectOffset(0, 6, 3, 2),
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 16,
        }, text);

        private static Texture2D Logo
        {
            get
            {
                if (_logo == null && !_logoResolved)
                {
                    _logoResolved = true;
                    _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoPath) ?? AssetDatabase.LoadAssetAtPath<Texture2D>(LogoFallbackPath);
                }
                return _logo;
            }
        }

        // ── Header ────────────────────────────────────────────────────────────────

        /// <summary>Cinematic film-strip banner with logo, wordmark, director/REC pill, and a clapperboard.</summary>
        public static void DrawHeader(string director, bool recording)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(HeaderHeight), GUILayout.ExpandWidth(true));
            Rect bar = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(3), GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(r, HeaderBg);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 13, r.width, r.height - 26), HeaderBand);
            for (float x = r.x + 6; x + 7 < r.xMax; x += 14)
            {
                EditorGUI.DrawRect(new Rect(x, r.y + 4, 7, 5), Sprocket);
                EditorGUI.DrawRect(new Rect(x, r.yMax - 9, 7, 5), Sprocket);
            }

            float half = Mathf.Floor(bar.width * 0.5f);
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, half, bar.height), PoliceRed);
            EditorGUI.DrawRect(new Rect(bar.x + half, bar.y, bar.width - half, bar.height), PoliceBlue);

            float x0 = r.x + 12;
            Texture2D logo = Logo;
            if (logo != null)
            {
                float lh = 40f, lw = lh * logo.width / Mathf.Max(1, logo.height);
                GUI.DrawTexture(new Rect(x0, r.y + 16, lw, lh), logo, ScaleMode.ScaleToFit, true);
                x0 += lw + 10;
                EditorGUI.DrawRect(new Rect(x0, r.y + 19, 1, 34), new Color(Accent.r, Accent.g, Accent.b, 0.55f));
                x0 += 11;
            }

            Rect clap = new(r.xMax - 12 - 44, r.y + 19, 44, 34);
            DrawClapper(clap);

            GUIStyle pillStyle = recording ? RedPill : DarkPill;
            var pill = new GUIContent(recording ? "\u25CF REC" : $"DIRECTOR  \u00B7  {director.ToUpperInvariant()}");
            Vector2 ps = pillStyle.CalcSize(pill);
            Rect pillRect = new(clap.x - 12 - ps.x, r.y + (r.height - 16) * 0.5f, ps.x, 16);

            var word = new GUIContent("FILM STUDIO");
            float wordW = Wordmark.CalcSize(word).x;
            bool showPill = pillRect.x > x0 + wordW + 8;
            float textRight = (showPill ? pillRect.x : clap.x) - 8;
            GUI.Label(new Rect(x0, r.y + 14, textRight - x0, 28), word, Wordmark);
            GUI.Label(new Rect(x0 + 1, r.y + 41, textRight - x0, 14), "GOOD COP BAD COP  \u00B7  SHORT-FORM DIRECTOR", SubMark);
            if (showPill) pillStyle.Draw(pillRect, pill, false, false, false, false);
        }

        private static void DrawClapper(Rect r)
        {
            var white = new Color(0.92f, 0.92f, 0.92f);
            var black = new Color(0.10f, 0.10f, 0.11f);
            Rect arm = new(r.x, r.y, r.width, 8);
            Rect body = new(r.x, r.y + 10, r.width, r.height - 10);

            EditorGUI.DrawRect(arm, black);
            for (int row = 0; row < (int)arm.height; row++)
            {
                float offset = (arm.height - row) * 0.9f;
                for (float sx = arm.x - 12 + offset; sx < arm.xMax; sx += 10)
                {
                    float a = Mathf.Max(sx, arm.x), b = Mathf.Min(sx + 5, arm.xMax);
                    if (b > a) EditorGUI.DrawRect(new Rect(a, arm.y + row, b - a, 1), white);
                }
            }
            EditorGUI.DrawRect(body, new Color(0.17f, 0.17f, 0.19f));
            EditorGUI.DrawRect(new Rect(body.x, body.y, body.width, 1), new Color(0.4f, 0.4f, 0.42f));
            EditorGUI.DrawRect(new Rect(body.x + 4, body.y + 8, body.width - 8, 1), new Color(0.38f, 0.38f, 0.40f));
            EditorGUI.DrawRect(new Rect(body.x + 4, body.y + 15, body.width - 8, 1), new Color(0.38f, 0.38f, 0.40f));
            EditorGUI.DrawRect(new Rect(body.x + body.width * 0.5f, body.y + 8, 1, body.height - 12), new Color(0.38f, 0.38f, 0.40f));
            EditorGUI.DrawRect(new Rect(body.x + 4, body.y + 3, 10, 3), Accent);
        }

        // ── Tabs ──────────────────────────────────────────────────────────────────

        /// <summary>Equal-width tab bar with an accent underline on the selected tab. Returns the (possibly new) index.</summary>
        public static int DrawTabs(int selected, string[] names)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(30), GUILayout.ExpandWidth(true));
            Event e = Event.current;
            float w = r.width / names.Length;
            int result = selected;

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, TabBarFill);
                EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), Divider);
            }
            for (int i = 0; i < names.Length; i++)
            {
                Rect cell = new(r.x + Mathf.Round(i * w), r.y, Mathf.Round((i + 1) * w) - Mathf.Round(i * w), r.height);
                bool sel = i == selected;
                if (e.type == EventType.Repaint)
                {
                    if (sel) EditorGUI.DrawRect(cell, CardFill);
                    else if (cell.Contains(e.mousePosition)) EditorGUI.DrawRect(cell, TabHover);
                    if (i > 0) EditorGUI.DrawRect(new Rect(cell.x, cell.y + 7, 1, cell.height - 14), Divider);
                    if (sel) EditorGUI.DrawRect(new Rect(cell.x, cell.yMax - 3, cell.width, 3), Accent);
                    (sel ? TabLabelSelected : TabLabel).Draw(cell, names[i], false, false, false, false);
                }
                EditorGUIUtility.AddCursorRect(cell, MouseCursor.Link);
                if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) result = i;
            }
            return result;
        }

        // ── Cards ─────────────────────────────────────────────────────────────────

        public readonly struct CardScope : IDisposable
        {
            private readonly Color _stripe;
            private readonly bool _hasStripe;
            public CardScope(Color? stripe) { _hasStripe = stripe.HasValue; _stripe = stripe ?? default; }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                if (_hasStripe && Event.current.type == EventType.Repaint)
                {
                    Rect r = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), _stripe);
                }
            }
        }

        /// <summary>Bordered panel with an optional accent-marked header and right-aligned hint.</summary>
        public static CardScope BeginCard(string title = null, string hint = null, Color? stripe = null)
        {
            EditorGUILayout.BeginVertical(Card);
            if (title != null) CardHeader(title, hint);
            return new CardScope(stripe);
        }

        public static void CardHeader(string title, string hint = null)
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, CardTitle, GUILayout.Height(20), GUILayout.ExpandWidth(true));
            var tc = new GUIContent(title);
            float tw = CardTitle.CalcSize(tc).x;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(r.x, r.y + 4, 3, 13), Accent);
                CardTitle.Draw(new Rect(r.x + 9, r.y, tw, r.height), tc, false, false, false, false);
                if (!string.IsNullOrEmpty(hint))
                    CardHint.Draw(new Rect(r.x + 17 + tw, r.y, Mathf.Max(0, r.width - tw - 17), r.height), hint, false, false, false, false);
            }
            Rect d = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(d, Divider);
            GUILayout.Space(5);
        }

        // ── Status ────────────────────────────────────────────────────────────────

        public static StatusLevel Classify(string s)
        {
            if (string.IsNullOrEmpty(s)) return StatusLevel.Info;
            bool Has(string k) => s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("fixing")) return StatusLevel.Warning;
            if (Has("failed") || Has("error")) return StatusLevel.Error;
            if (Has("cancel") || Has("empty") || Has("needs") || Has("add a note") || Has("still has")) return StatusLevel.Warning;
            if (Has("built") || Has("recorded") || Has("generated") || Has("revised") || Has("loaded") ||
                Has("adopted") || Has("rebuilt") || Has("posted")) return StatusLevel.Success;
            return StatusLevel.Info;
        }

        /// <summary>Colour-coded status line (tinted background + left bar), level inferred from the text.</summary>
        public static void DrawStatus(string text)
        {
            var c = new GUIContent(text);
            Rect r = GUILayoutUtility.GetRect(c, StatusText, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            Color col = StatusColor(Classify(text));
            EditorGUI.DrawRect(r, new Color(col.r, col.g, col.b, Pro ? 0.13f : 0.18f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), col);
            StatusText.Draw(r, c, false, false, false, false);
        }

        public static void DrawRowBackground(Rect r, Color c)
        {
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, c);
        }
    }
}
