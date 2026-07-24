using System;
using UnityEngine;

namespace GoodCopBadCop.EnvironmentSystem
{
    [CreateAssetMenu(fileName = "EnvironmentSchedule", menuName = "GoodCopBadCop/Environment Schedule")]
    public sealed class EnvironmentSchedule : ScriptableObject
    {
        [Serializable]
        public struct ScheduleEntry
        {
            [Tooltip("Applied at the start of the shift (morning/day look).")]
            public EnvironmentPreset preset;

            [Tooltip("Target look the environment progressively lerps toward as the shift's suspect lineup is processed. Leave empty to keep the day preset fixed for the whole shift.")]
            public EnvironmentPreset nightPreset;

            public bool rainEnabled;
        }

        [SerializeField] private ScheduleEntry[] dayLoop;

        [Tooltip("Seconds it takes the environment to catch up to the current day/night target blend after each suspect is processed.")]
        [SerializeField, Min(0.01f)] private float dayNightBlendSeconds = 1.5f;

        public float DayNightBlendSeconds => dayNightBlendSeconds;

        public int Count => dayLoop != null ? dayLoop.Length : 0;

        public EnvironmentPreset GetPresetForDay(int day)
        {
            if (dayLoop == null || dayLoop.Length == 0)
            {
                return null;
            }

            int safeDay = Mathf.Max(1, day);
            int index = (safeDay - 1) % dayLoop.Length;
            return dayLoop[index].preset;
        }

        public bool GetRainEnabledForDay(int day)
        {
            if (dayLoop == null || dayLoop.Length == 0)
            {
                return false;
            }

            int safeDay = Mathf.Max(1, day);
            int index = (safeDay - 1) % dayLoop.Length;
            return dayLoop[index].rainEnabled;
        }

        public EnvironmentPreset GetPresetAtLoopIndex(int index)
        {
            if (dayLoop == null || dayLoop.Length == 0)
            {
                return null;
            }

            int wrappedIndex = ((index % dayLoop.Length) + dayLoop.Length) % dayLoop.Length;
            return dayLoop[wrappedIndex].preset;
        }

        /// <summary>
        /// The environment preset the shift should be fully lerped to by the time the last
        /// suspect in the lineup has been processed. Returns null when the day has no night
        /// preset configured, in which case the day preset stays fixed for the whole shift.
        /// </summary>
        public EnvironmentPreset GetNightPresetForDay(int day)
        {
            if (dayLoop == null || dayLoop.Length == 0)
            {
                return null;
            }

            int safeDay = Mathf.Max(1, day);
            int index = (safeDay - 1) % dayLoop.Length;
            return dayLoop[index].nightPreset;
        }

        public EnvironmentPreset GetNightPresetAtLoopIndex(int index)
        {
            if (dayLoop == null || dayLoop.Length == 0)
            {
                return null;
            }

            int wrappedIndex = ((index % dayLoop.Length) + dayLoop.Length) % dayLoop.Length;
            return dayLoop[wrappedIndex].nightPreset;
        }
    }

}
