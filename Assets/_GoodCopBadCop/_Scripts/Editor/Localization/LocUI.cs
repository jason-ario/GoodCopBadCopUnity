using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Small IMGUI style kit for the localization tools: cards, pills, tabs, stat tiles. Skin aware, no external dependencies.</summary>
public static class LocUI
{
    public static bool Dark { get { return EditorGUIUtility.isProSkin; } }

    public static readonly Color Accent = new Color(0.26f, 0.60f, 0.98f);
    public static readonly Color Ok = new Color(0.30f, 0.72f, 0.42f);
    public static readonly Color Warn = new Color(0.93f, 0.66f, 0.20f);
    public static readonly Color Bad = new Color(0.88f, 0.36f, 0.33f);
    public static readonly Color Neutral = new Color(0.50f, 0.54f, 0.60f);
    public static readonly Color Violet = new Color(0.62f, 0.45f, 0.92f);

    public static Color HeaderBg { get { return Dark ? new Color(0.11f, 0.14f, 0.20f) : new Color(0.20f, 0.30f, 0.47f); } }
    public static Color CardBg { get { return Dark ? new Color(0.20f, 0.21f, 0.23f) : new Color(0.93f, 0.94f, 0.95f); } }
    public static Color PageBg { get { return Dark ? new Color(0.16f, 0.17f, 0.18f) : new Color(0.80f, 0.81f, 0.83f); } }
    public static Color Muted { get { return Dark ? new Color(0.62f, 0.65f, 0.70f) : new Color(0.38f, 0.40f, 0.45f); } }

    private static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<Color, GUIStyle> PillStyles = new Dictionary<Color, GUIStyle>();
    private static GUIStyle _card, _body, _title, _subtitle, _section, _sectionSub, _tab, _hint, _muted, _tileValue, _tileLabel, _status, _headerBtn;

    // ---------------------------------------------------------------- textures

    private static Texture2D Rounded(Color c, int radius)
    {
        string id = ColorUtility.ToHtmlStringRGBA(c) + radius;
        if (Textures.TryGetValue(id, out var cached) && cached != null) return cached;

        int s = radius * 2 + 2;
        var t = new Texture2D(s, s, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (s - radius), 0f);
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (s - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(radius - d + 0.5f);
                t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * a));
            }
        t.Apply();
        Textures[id] = t;
        return t;
    }

    public static Texture2D Solid(Color c)
    {
        string id = "solid" + ColorUtility.ToHtmlStringRGBA(c);
        if (Textures.TryGetValue(id, out var cached) && cached != null) return cached;
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        Textures[id] = t;
        return t;
    }

    // ---------------------------------------------------------------- styles

    public static void EnsureStyles()
    {
        if (_card != null && _card.normal.background != null) return;
        Textures.Clear();
        PillStyles.Clear();

        _card = new GUIStyle
        {
            normal = { background = Rounded(CardBg, 6) },
            border = new RectOffset(7, 7, 7, 7),
            padding = new RectOffset(10, 10, 8, 8),
            margin = new RectOffset(2, 2, 3, 3)
        };
        _body = new GUIStyle { padding = new RectOffset(12, 12, 8, 8) };
        _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, normal = { textColor = Color.white } };
        _subtitle = new GUIStyle(EditorStyles.label) { fontSize = 11, normal = { textColor = new Color(0.78f, 0.84f, 0.95f) } };
        _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, margin = new RectOffset(2, 2, 10, 2) };
        _sectionSub = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = Muted } };
        _tab = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter, fontSize = 12, fixedHeight = 30,
            padding = new RectOffset(14, 14, 0, 0), normal = { textColor = Muted }, hover = { textColor = Dark ? Color.white : Color.black }
        };
        _hint = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true, richText = true, fontSize = 11, normal = { textColor = Muted },
            padding = new RectOffset(2, 2, 2, 2)
        };
        _muted = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Muted }, wordWrap = true };
        _tileValue = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        _tileLabel = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Muted } };
        _status = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 11, padding = new RectOffset(10, 10, 4, 4), normal = { textColor = Color.white } };
        _headerBtn = new GUIStyle(GUI.skin.button) { fontSize = 11, fixedHeight = 24 };
    }

    public static GUIStyle CardStyle { get { EnsureStyles(); return _card; } }
    public static GUIStyle BodyStyle { get { EnsureStyles(); return _body; } }
    public static GUIStyle MutedStyle { get { EnsureStyles(); return _muted; } }

    private static GUIStyle PillStyle(Color c)
    {
        if (PillStyles.TryGetValue(c, out var s) && s.normal.background != null) return s;
        s = new GUIStyle
        {
            normal = { background = Rounded(c, 8), textColor = Color.white },
            border = new RectOffset(9, 9, 9, 9),
            padding = new RectOffset(8, 8, 1, 2),
            margin = new RectOffset(2, 2, 2, 2),
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            fixedHeight = 17,
            stretchWidth = false
        };
        PillStyles[c] = s;
        return s;
    }

    // ---------------------------------------------------------------- widgets

    public sealed class CardScope : IDisposable
    {
        private readonly Color? _accent;
        public CardScope(Color? accent = null, params GUILayoutOption[] options)
        {
            EnsureStyles();
            _accent = accent;
            EditorGUILayout.BeginVertical(_card, options);
        }

        public void Dispose()
        {
            EditorGUILayout.EndVertical();
            if (_accent.HasValue && Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                GUI.DrawTexture(new Rect(r.x + 1, r.y + 6, 3, r.height - 12), Solid(_accent.Value));
            }
        }
    }

    public static CardScope Card(Color? accent = null, params GUILayoutOption[] options) { return new CardScope(accent, options); }

    public static void Pill(string text, Color c, float minWidth = 0f)
    {
        EnsureStyles();
        if (minWidth > 0f) GUILayout.Label(text, PillStyle(c), GUILayout.MinWidth(minWidth));
        else GUILayout.Label(text, PillStyle(c));
    }

    public static void Section(string title, string subtitle = null)
    {
        EnsureStyles();
        GUILayout.Label(title, _section);
        if (!string.IsNullOrEmpty(subtitle)) GUILayout.Label(subtitle, _sectionSub);
        var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, Solid(new Color(Muted.r, Muted.g, Muted.b, 0.35f)));
        GUILayout.Space(4);
    }

    public static void Hint(string message)
    {
        EnsureStyles();
        using (Card(Accent)) GUILayout.Label(message, _hint);
    }

    public static void Muted_(string message) { EnsureStyles(); GUILayout.Label(message, _muted); }

    public static bool Button(string text, bool primary = false, params GUILayoutOption[] options)
    {
        var prev = GUI.backgroundColor;
        if (primary) GUI.backgroundColor = Accent;
        bool clicked = GUILayout.Button(text, GUILayout.Height(22), options.Length == 0 ? GUILayout.ExpandWidth(false) : options[0]);
        GUI.backgroundColor = prev;
        return clicked;
    }

    public static bool Chip(bool value, string text, Color? color = null)
    {
        EnsureStyles();
        var c = color ?? Accent;
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = value ? c : new Color(0.5f, 0.5f, 0.5f, 0.6f);
        bool nv = GUILayout.Toggle(value, text, EditorStyles.miniButton, GUILayout.Height(20));
        GUI.backgroundColor = prev;
        return nv;
    }

    public static int Tabs(int selected, string[] names)
    {
        EnsureStyles();
        var bar = EditorGUILayout.BeginHorizontal();
        if (Event.current.type == EventType.Repaint)
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width, 30), Solid(Dark ? new Color(0.14f, 0.15f, 0.16f) : new Color(0.72f, 0.73f, 0.75f)));
        for (int i = 0; i < names.Length; i++)
        {
            var content = new GUIContent(names[i]);
            var r = GUILayoutUtility.GetRect(content, _tab, GUILayout.ExpandWidth(false));
            bool sel = i == selected;
            if (Event.current.type == EventType.Repaint)
            {
                var style = new GUIStyle(_tab);
                if (sel) style.normal.textColor = Dark ? Color.white : Color.black;
                bool hover = r.Contains(Event.current.mousePosition);
                if (hover && !sel) style.normal.textColor = Dark ? new Color(0.85f, 0.88f, 0.92f) : Color.black;
                style.Draw(r, content, false, false, false, false);
                if (sel) GUI.DrawTexture(new Rect(r.x + 6, r.yMax - 3, r.width - 12, 3), Solid(Accent));
            }
            if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition))
            {
                selected = i;
                Event.current.Use();
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        return selected;
    }

    public static void StatTile(string label, string value, Color accent, float width = 140f)
    {
        EnsureStyles();
        using (Card(accent, GUILayout.Width(width)))
        {
            GUILayout.Label(value, _tileValue);
            GUILayout.Label(label, _tileLabel);
        }
    }

    public static void StatusBar(string message, Color color)
    {
        EnsureStyles();
        var r = EditorGUILayout.BeginHorizontal();
        if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, Solid(new Color(color.r * 0.75f, color.g * 0.75f, color.b * 0.75f, 1f)));
        GUILayout.Label(message, _status);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Draws the page banner. Returns the rect so callers can place buttons inside it.</summary>
    public static Rect Header(string title, string subtitle)
    {
        EnsureStyles();
        var r = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
        {
            GUI.DrawTexture(r, Solid(HeaderBg));
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), Solid(Accent));
        }
        GUI.Label(new Rect(r.x + 14, r.y + 7, r.width - 300, 26), title, _title);
        GUI.Label(new Rect(r.x + 14, r.y + 31, r.width - 300, 18), subtitle, _subtitle);
        return r;
    }

    public static bool HeaderButton(ref float xRight, Rect header, string text, bool primary, float width)
    {
        EnsureStyles();
        xRight -= width + 6;
        var prev = GUI.backgroundColor;
        if (primary) GUI.backgroundColor = Accent;
        bool clicked = GUI.Button(new Rect(xRight, header.y + 16, width, 24), text, _headerBtn);
        GUI.backgroundColor = prev;
        return clicked;
    }
}
