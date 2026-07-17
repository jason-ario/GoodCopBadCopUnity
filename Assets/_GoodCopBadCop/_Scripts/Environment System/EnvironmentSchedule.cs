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
            public EnvironmentPreset preset;
            public bool rainEnabled;
        }

        [SerializeField] private ScheduleEntry[] dayLoop;

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
    }

}
