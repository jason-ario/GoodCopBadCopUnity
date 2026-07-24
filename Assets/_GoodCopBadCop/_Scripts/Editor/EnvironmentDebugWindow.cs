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

        [SerializeField]
        private EnvironmentPreset nightPresetToApply;

        [SerializeField, Range(0f, 1f)]
        private float suspectProgressToApply;

        private EnvironmentSchedule environmentSchedule;
        private IEnvironmentService service;
        private IEnvironmentModel model;
        private IDisposable currentDaySubscription;
        private IDisposable currentPresetSubscription;
        private IDisposable currentNightPresetSubscription;
        private IDisposable dayNightProgressSubscription;
        private string status = "Not connected.";
        private int currentDay;
        private EnvironmentPreset currentPreset;
        private EnvironmentPreset currentNightPreset;
        private float currentDayNightProgress;

        /// <summary>
        /// Tracks whether Apply Next/Apply Previous are currently parked on the night half of
        /// <see cref="currentDay"/>, so stepping interleaves each day's morning and night look
        /// instead of jumping straight from one day's morning preset to the next day's.
        /// </summary>
        private bool isNightPhase;

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

        [ShowInInspector, ReadOnly, PropertyOrder(-8)]
        private EnvironmentPreset CurrentNightPreset => currentNightPreset;

        [ShowInInspector, ReadOnly, PropertyOrder(-7)]
        private float CurrentDayNightProgress => currentDayNightProgress;

        [ShowInInspector, ReadOnly, PropertyOrder(-6)]
        private EnvironmentPreset ScheduledNightPresetForDayToApply => environmentSchedule != null
            ? environmentSchedule.GetNightPresetForDay(dayToApply)
            : null;

        [ShowInInspector, ReadOnly, PropertyOrder(-5)]
        private string SteppingPhase => isNightPhase ? "Night" : "Day";

        [Button(ButtonSizes.Large), HorizontalGroup("Navigation"), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyPrevious()
        {
            if (service == null)
            {
                return;
            }

            if (isNightPhase)
            {
                // Step back from this day's night look to its own morning look first.
                service.ForceSuspectProgress(0, 1);
                isNightPhase = false;
            }
            else
            {
                // Step back into the previous day, parked on its night look.
                service.ApplyPrevious();
                service.ForceSuspectProgress(1, 1);
                isNightPhase = true;
            }
        }

        [Button(ButtonSizes.Large), HorizontalGroup("Navigation"), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyNext()
        {
            if (service == null)
            {
                return;
            }

            if (isNightPhase)
            {
                // Step forward into the next day, starting on its morning look.
                service.ApplyNext();
                isNightPhase = false;
            }
            else
            {
                // Step forward from this day's morning look to its own night look.
                service.ForceSuspectProgress(1, 1);
                isNightPhase = true;
            }
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplyDay()
        {
            service?.ApplyDay(dayToApply);
            isNightPhase = false;
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanApplySelectedPreset))]
        private void ApplySelectedPreset()
        {
            service?.ApplyPreset(presetToApply);
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanApplySelectedNightPreset))]
        private void ApplySelectedNightPreset()
        {
            service?.ApplyNightPreset(nightPresetToApply);
        }

        [Button(ButtonSizes.Medium), EnableIf(nameof(CanUseRuntimeServices))]
        private void ApplySuspectProgress()
        {
            service?.ForceSuspectProgress(Mathf.RoundToInt(suspectProgressToApply * 100f), 100);
            isNightPhase = suspectProgressToApply >= 0.5f;
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
            currentNightPreset = null;
            currentDayNightProgress = 0f;

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
            currentNightPreset = model.CurrentNightPreset.CurrentValue;
            currentDayNightProgress = model.DayNightProgress.CurrentValue;
            isNightPhase = currentDayNightProgress >= 0.5f;
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

            currentNightPresetSubscription = model.CurrentNightPreset.Subscribe(preset =>
            {
                currentNightPreset = preset;
                Repaint();
            });

            dayNightProgressSubscription = model.DayNightProgress.Subscribe(progress =>
            {
                currentDayNightProgress = progress;
                Repaint();
            });
        }

        private void DisposeSubscriptions()
        {
            currentDaySubscription?.Dispose();
            currentPresetSubscription?.Dispose();
            currentNightPresetSubscription?.Dispose();
            dayNightProgressSubscription?.Dispose();
            currentDaySubscription = null;
            currentPresetSubscription = null;
            currentNightPresetSubscription = null;
            dayNightProgressSubscription = null;
        }

        private void ClearRuntimeReferences(string newStatus)
        {
            DisposeSubscriptions();
            environmentSchedule = null;
            service = null;
            model = null;
            currentDay = 0;
            currentPreset = null;
            currentNightPreset = null;
            currentDayNightProgress = 0f;
            isNightPhase = false;
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

        private bool CanApplySelectedNightPreset()
        {
            return nightPresetToApply != null && CanUseRuntimeServices();
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
