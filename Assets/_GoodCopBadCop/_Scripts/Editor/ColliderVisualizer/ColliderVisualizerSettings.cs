using System;
using UnityEditor;
using UnityEngine;

namespace GoodCopBadCop.Editor.ColliderVisualization
{
    internal enum ColliderPickMode
    {
        Click = 0,
        ActionKeyClick = 1,
        Off = 2
    }

    /// <summary>
    /// EditorPrefs-backed settings for the Collider Visualizer. Values are cached in memory so the
    /// Scene view drawing code can read them every frame without hitting EditorPrefs.
    /// </summary>
    internal static class ColliderVisualizerSettings
    {
        private const string Prefix = "GCBC.ColliderVisualizer.";

        public static event Action Changed;

        private static bool enabled;
        private static bool showSolid;
        private static bool showTriggers;
        private static bool drawFill;
        private static bool xRay;
        private static bool showHoverLabel;
        private static int layerMask;
        private static int pickMode;
        private static int maxMeshTriangles;
        private static float fillAlpha;
        private static float maxDistance;
        private static Color solidColor;
        private static Color triggerColor;
        private static Color rigidbodyColor;
        private static Color hoverColor;
        private static Color selectedColor;

        static ColliderVisualizerSettings()
        {
            Load();
        }

        public static bool Enabled { get => enabled; set => Set(ref enabled, value, nameof(Enabled)); }
        public static bool ShowSolid { get => showSolid; set => Set(ref showSolid, value, nameof(ShowSolid)); }
        public static bool ShowTriggers { get => showTriggers; set => Set(ref showTriggers, value, nameof(ShowTriggers)); }
        public static bool DrawFill { get => drawFill; set => Set(ref drawFill, value, nameof(DrawFill)); }
        public static bool XRay { get => xRay; set => Set(ref xRay, value, nameof(XRay)); }
        public static bool ShowHoverLabel { get => showHoverLabel; set => Set(ref showHoverLabel, value, nameof(ShowHoverLabel)); }
        public static int LayerMask { get => layerMask; set => Set(ref layerMask, value, nameof(LayerMask)); }
        public static ColliderPickMode PickMode { get => (ColliderPickMode)pickMode; set => Set(ref pickMode, (int)value, nameof(PickMode)); }
        public static int MaxMeshTriangles { get => maxMeshTriangles; set => Set(ref maxMeshTriangles, Mathf.Max(0, value), nameof(MaxMeshTriangles)); }
        public static float FillAlpha { get => fillAlpha; set => Set(ref fillAlpha, Mathf.Clamp01(value), nameof(FillAlpha)); }
        public static float MaxDistance { get => maxDistance; set => Set(ref maxDistance, Mathf.Max(0f, value), nameof(MaxDistance)); }
        public static Color SolidColor { get => solidColor; set => Set(ref solidColor, value, nameof(SolidColor)); }
        public static Color TriggerColor { get => triggerColor; set => Set(ref triggerColor, value, nameof(TriggerColor)); }
        public static Color RigidbodyColor { get => rigidbodyColor; set => Set(ref rigidbodyColor, value, nameof(RigidbodyColor)); }
        public static Color HoverColor { get => hoverColor; set => Set(ref hoverColor, value, nameof(HoverColor)); }
        public static Color SelectedColor { get => selectedColor; set => Set(ref selectedColor, value, nameof(SelectedColor)); }

        public static void ResetToDefaults()
        {
            foreach (var key in new[]
                     {
                         nameof(ShowSolid), nameof(ShowTriggers), nameof(DrawFill), nameof(XRay), nameof(ShowHoverLabel),
                         nameof(LayerMask), nameof(PickMode), nameof(MaxMeshTriangles), nameof(FillAlpha), nameof(MaxDistance),
                         nameof(SolidColor), nameof(TriggerColor), nameof(RigidbodyColor), nameof(HoverColor), nameof(SelectedColor)
                     })
            {
                EditorPrefs.DeleteKey(Prefix + key);
            }

            Load();
            Changed?.Invoke();
        }

        private static void Load()
        {
            enabled = EditorPrefs.GetBool(Prefix + nameof(Enabled), false);
            showSolid = EditorPrefs.GetBool(Prefix + nameof(ShowSolid), true);
            showTriggers = EditorPrefs.GetBool(Prefix + nameof(ShowTriggers), true);
            drawFill = EditorPrefs.GetBool(Prefix + nameof(DrawFill), true);
            xRay = EditorPrefs.GetBool(Prefix + nameof(XRay), true);
            showHoverLabel = EditorPrefs.GetBool(Prefix + nameof(ShowHoverLabel), true);
            layerMask = EditorPrefs.GetInt(Prefix + nameof(LayerMask), ~0);
            pickMode = EditorPrefs.GetInt(Prefix + nameof(PickMode), (int)ColliderPickMode.Click);
            maxMeshTriangles = EditorPrefs.GetInt(Prefix + nameof(MaxMeshTriangles), 20000);
            fillAlpha = EditorPrefs.GetFloat(Prefix + nameof(FillAlpha), 0.08f);
            maxDistance = EditorPrefs.GetFloat(Prefix + nameof(MaxDistance), 0f);
            solidColor = GetColor(nameof(SolidColor), new Color(0.35f, 1f, 0.35f, 0.9f));
            triggerColor = GetColor(nameof(TriggerColor), new Color(0.3f, 0.75f, 1f, 0.9f));
            rigidbodyColor = GetColor(nameof(RigidbodyColor), new Color(1f, 0.6f, 0.15f, 0.9f));
            hoverColor = GetColor(nameof(HoverColor), new Color(1f, 0.95f, 0.2f, 1f));
            selectedColor = GetColor(nameof(SelectedColor), Color.white);
        }

        private static Color GetColor(string key, Color fallback)
        {
            var html = EditorPrefs.GetString(Prefix + key, string.Empty);
            return !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString("#" + html, out var color) ? color : fallback;
        }

        private static void Set(ref bool field, bool value, string key)
        {
            if (field == value) return;
            field = value;
            EditorPrefs.SetBool(Prefix + key, value);
            Changed?.Invoke();
        }

        private static void Set(ref int field, int value, string key)
        {
            if (field == value) return;
            field = value;
            EditorPrefs.SetInt(Prefix + key, value);
            Changed?.Invoke();
        }

        private static void Set(ref float field, float value, string key)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            EditorPrefs.SetFloat(Prefix + key, value);
            Changed?.Invoke();
        }

        private static void Set(ref Color field, Color value, string key)
        {
            if (field == value) return;
            field = value;
            EditorPrefs.SetString(Prefix + key, ColorUtility.ToHtmlStringRGBA(value));
            Changed?.Invoke();
        }
    }
}
