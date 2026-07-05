using GoodCopBadCop.Infrastructure;
using GoodCopBadCop.EnvironmentSystem;
using System;
using R3;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace GoodCopBadCop.Editor
{
    public sealed class EnvironmentDebugWindow : OdinEditorWindow
    {
        [SerializeField, MinValue(1)]
        private int dayToApply = 1;

        [SerializeField]
        private EnvironmentPreset presetToApply;

        private EnvironmentSchedule environmentSchedule;
        private IEnvironmentService service;
        private IEnvironmentModel model;
        private IDisposable currentDaySubscription;
        private IDisposable currentPresetSubscription;
        private string status = "Not connected.";
        private int currentDay;
        private EnvironmentPreset currentPreset;

        [MenuItem(EditorConstants.EnvironmentDebugMenuPath, false, EditorConstants.RootMenuPriority)]
        private static void Open()
        {
            EnvironmentDebugWindow window = GetWindow<EnvironmentDebugWindow>();
            window.titleContent = new GUIContent("Environment Debug");
            window.minSize = new Vector2(320f, 220f);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            RefreshSubscriptions();
        }

        protected override void OnDestroy()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            DisposeSubscriptions();
            base.OnDestroy();
        }

        [ShowInInspector, ReadOnly, PropertyOrder(-20)]
        private string Status => status;

        [ShowInInspector, ReadOnly, PropertyOrder(-15)]
        private EnvironmentSchedule EnvironmentSequence => environmentSchedule;

        [ShowInInspector, ReadOnly, PropertyOrder(-10)]
        private int CurrentDay => currentDay;

        [ShowInInspector, ReadOnly, PropertyOrder(-9)]
        private EnvironmentPreset CurrentPreset => currentPreset;

        [Button(ButtonSizes.Large), HorizontalGroup("Navigation"), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyPrevious()
        {
            service?.ApplyPrevious();
        }

        [Button(ButtonSizes.Large), HorizontalGroup("Navigation"), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyNext()
        {
            service?.ApplyNext();
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyDay()
        {
            service?.ApplyDay(dayToApply);
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanApplySelectedPreset))]
        private void ApplySelectedPreset()
        {
            service?.ApplyPreset(presetToApply);
        }

        [Button(ButtonSizes.Small)]
        private void Refresh()
        {
            RefreshSubscriptions();
            Repaint();
        }

        private void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            if (stateChange == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += Refresh;
            }
            else if (stateChange == PlayModeStateChange.ExitingPlayMode
                     || stateChange == PlayModeStateChange.EnteredEditMode)
            {
                ClearRuntimeReferences("Enter Play Mode to use runtime environment services.");
            }

            Repaint();
        }

        private void RefreshSubscriptions()
        {
            DisposeSubscriptions();

            environmentSchedule = null;
            service = null;
            model = null;
            currentDay = 0;
            currentPreset = null;

            if (!EditorApplication.isPlaying)
            {
                status = "Enter Play Mode to use runtime environment services.";
                return;
            }

            if (!TryGetContainer(out IObjectResolver container))
            {
                status = "MainSceneLifetimeScope or its VContainer runtime container was not found.";
                return;
            }

            container.TryResolve(out environmentSchedule);

            if (!container.TryResolve(out service))
            {
                status = "IEnvironmentService is not registered in the active VContainer scope.";
                return;
            }

            if (!container.TryResolve(out model))
            {
                status = "IEnvironmentModel is not registered in the active VContainer scope.";
                return;
            }

            currentDay = model.CurrentDay.CurrentValue;
            currentPreset = model.CurrentPreset.CurrentValue;
            status = "Ready.";

            currentDaySubscription = model.CurrentDay.Subscribe(day =>
            {
                currentDay = day;
                Repaint();
            });

            currentPresetSubscription = model.CurrentPreset.Subscribe(preset =>
            {
                currentPreset = preset;
                Repaint();
            });
        }

        private void DisposeSubscriptions()
        {
            currentDaySubscription?.Dispose();
            currentPresetSubscription?.Dispose();
            currentDaySubscription = null;
            currentPresetSubscription = null;
        }

        private void ClearRuntimeReferences(string newStatus)
        {
            DisposeSubscriptions();
            environmentSchedule = null;
            service = null;
            model = null;
            currentDay = 0;
            currentPreset = null;
            status = newStatus;
        }

        private bool CanUseRuntimeServices()
        {
            return EditorApplication.isPlaying && service != null;
        }

        private bool CanApplySelectedPreset()
        {
            return presetToApply != null && CanUseRuntimeServices();
        }

        private static bool TryGetContainer(out IObjectResolver container)
        {
            container = null;

            MainSceneLifetimeScope scope = UnityEngine.Object.FindFirstObjectByType<MainSceneLifetimeScope>();
            if (scope == null || scope.Container == null)
            {
                return false;
            }

            container = scope.Container;
            return true;
        }
    }

}
