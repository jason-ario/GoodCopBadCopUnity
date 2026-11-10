using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.ShortcutManagement;
using UnityEditor.Toolbars;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoodCopBadCop.Editor.ColliderVisualization
{
    internal sealed class ColliderVisualizerWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem(EditorConstants.ColliderVisualizerMenuPath)]
        public static void Open()
        {
            var window = GetWindow<ColliderVisualizerWindow>();
            window.titleContent = new GUIContent("Collider Visualizer", EditorGUIUtility.IconContent("BoxCollider Icon").image);
            window.minSize = new Vector2(280f, 320f);
            window.Show();
        }

        [Shortcut("Good Cop Bad Cop/Toggle Collider Visualizer", KeyCode.C, ShortcutModifiers.Alt | ShortcutModifiers.Shift)]
        public static void Toggle()
        {
            ColliderVisualizerSettings.Enabled = !ColliderVisualizerSettings.Enabled;
        }

        private void OnEnable()
        {
            ColliderVisualizerSettings.Changed += Repaint;
        }

        private void OnDisable()
        {
            ColliderVisualizerSettings.Changed -= Repaint;
        }

        private void OnInspectorUpdate()
        {
            if (ColliderVisualizerSettings.Enabled) Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            using (new EditorGUILayout.HorizontalScope())
            {
                var enabled = GUILayout.Toggle(ColliderVisualizerSettings.Enabled,
                    ColliderVisualizerSettings.Enabled ? "Visualizer ON" : "Visualizer OFF", "Button", GUILayout.Height(28f));
                ColliderVisualizerSettings.Enabled = enabled;
            }

            EditorGUILayout.LabelField($"Drawn: {ColliderVisualizer.LastDrawnCount}   Tracked in stage: {ColliderVisualizer.TrackedCount}", EditorStyles.miniLabel);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Filter", EditorStyles.boldLabel);
            ColliderVisualizerSettings.ShowSolid = EditorGUILayout.Toggle("Solid Colliders", ColliderVisualizerSettings.ShowSolid);
            ColliderVisualizerSettings.ShowTriggers = EditorGUILayout.Toggle("Triggers", ColliderVisualizerSettings.ShowTriggers);
            var concatenated = InternalEditorUtility.LayerMaskToConcatenatedLayersMask(ColliderVisualizerSettings.LayerMask);
            concatenated = EditorGUILayout.MaskField("Layers", concatenated, InternalEditorUtility.layers);
            ColliderVisualizerSettings.LayerMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(concatenated);
            ColliderVisualizerSettings.MaxDistance = EditorGUILayout.FloatField(
                new GUIContent("Max Distance", "Only draw/pick colliders within this distance of the Scene camera. 0 = unlimited."),
                ColliderVisualizerSettings.MaxDistance);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            ColliderVisualizerSettings.XRay = EditorGUILayout.Toggle(
                new GUIContent("Draw Through Geometry", "Show colliders even when hidden behind other geometry."),
                ColliderVisualizerSettings.XRay);
            ColliderVisualizerSettings.DrawFill = EditorGUILayout.Toggle("Translucent Fill", ColliderVisualizerSettings.DrawFill);
            using (new EditorGUI.DisabledScope(!ColliderVisualizerSettings.DrawFill))
            {
                ColliderVisualizerSettings.FillAlpha = EditorGUILayout.Slider("Fill Opacity", ColliderVisualizerSettings.FillAlpha, 0f, 0.5f);
            }

            ColliderVisualizerSettings.ShowHoverLabel = EditorGUILayout.Toggle("Hover Label", ColliderVisualizerSettings.ShowHoverLabel);

            EditorGUI.BeginChangeCheck();
            ColliderVisualizerSettings.MaxMeshTriangles = EditorGUILayout.DelayedIntField(
                new GUIContent("Mesh Wire Tri Limit", "Mesh colliders above this triangle count draw as bounds instead of a wireframe."),
                ColliderVisualizerSettings.MaxMeshTriangles);
            if (EditorGUI.EndChangeCheck()) ColliderVisualizer.ClearMeshCache();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Colors", EditorStyles.boldLabel);
            ColliderVisualizerSettings.SolidColor = EditorGUILayout.ColorField("Static / Kinematic", ColliderVisualizerSettings.SolidColor);
            ColliderVisualizerSettings.RigidbodyColor = EditorGUILayout.ColorField("Dynamic Rigidbody", ColliderVisualizerSettings.RigidbodyColor);
            ColliderVisualizerSettings.TriggerColor = EditorGUILayout.ColorField("Trigger", ColliderVisualizerSettings.TriggerColor);
            ColliderVisualizerSettings.HoverColor = EditorGUILayout.ColorField("Hovered", ColliderVisualizerSettings.HoverColor);
            ColliderVisualizerSettings.SelectedColor = EditorGUILayout.ColorField("Selected", ColliderVisualizerSettings.SelectedColor);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Picking", EditorStyles.boldLabel);
            ColliderVisualizerSettings.PickMode = (ColliderPickMode)EditorGUILayout.EnumPopup(
                new GUIContent("Pick Mode", "Click: plain click selects colliders. ActionKeyClick: Ctrl/Cmd+Click. Off: draw only."),
                ColliderVisualizerSettings.PickMode);
            EditorGUILayout.HelpBox(
                "Click a collider in the Scene view to select its GameObject. Click the same spot again to cycle through overlapping colliders. " +
                "Shift+Click adds/removes from the selection. Hidden or pick-disabled objects (Scene visibility) are ignored.\n" +
                "Toggle anytime with Alt+Shift+C or the Scene view overlay.",
                MessageType.None);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh"))
                {
                    ColliderVisualizer.MarkDirty();
                    ColliderVisualizer.ClearMeshCache();
                }

                if (GUILayout.Button("Reset Settings")) ColliderVisualizerSettings.ResetToDefaults();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    [Overlay(typeof(SceneView), OverlayId, "Collider Visualizer", defaultDisplay = true)]
    internal sealed class ColliderVisualizerOverlay : ToolbarOverlay
    {
        public const string OverlayId = "gcbc-collider-visualizer";

        private ColliderVisualizerOverlay() : base(ColliderVisualizerToggle.Id, ColliderVisualizerSettingsButton.Id)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class ColliderVisualizerToggle : EditorToolbarToggle
    {
        public const string Id = "GCBC/ColliderVisualizer/Toggle";

        public ColliderVisualizerToggle()
        {
            icon = EditorGUIUtility.IconContent("BoxCollider Icon").image as Texture2D;
            tooltip = "Show all colliders and click them to select (Alt+Shift+C)";
            SetValueWithoutNotify(ColliderVisualizerSettings.Enabled);
            this.RegisterValueChangedCallback(evt => ColliderVisualizerSettings.Enabled = evt.newValue);
            RegisterCallback<AttachToPanelEvent>(_ => ColliderVisualizerSettings.Changed += Sync);
            RegisterCallback<DetachFromPanelEvent>(_ => ColliderVisualizerSettings.Changed -= Sync);
        }

        private void Sync() => SetValueWithoutNotify(ColliderVisualizerSettings.Enabled);
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal sealed class ColliderVisualizerSettingsButton : EditorToolbarButton
    {
        public const string Id = "GCBC/ColliderVisualizer/Settings";

        public ColliderVisualizerSettingsButton()
        {
            icon = EditorGUIUtility.IconContent("SettingsIcon").image as Texture2D;
            tooltip = "Collider Visualizer settings";
            clicked += ColliderVisualizerWindow.Open;
        }
    }
}
