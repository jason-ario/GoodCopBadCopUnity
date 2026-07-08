using System.Collections.Generic;
using UnityEngine;

namespace GoodCopBadCop.Effects
{
    public interface IEffectCatalog
    {
        IReadOnlyList<EffectPreset> Presets { get; }
        bool TryGet(string key, out EffectPreset preset);
    }

    [CreateAssetMenu(menuName = "GoodCopBadCop/Effects/Effect Catalog", fileName = "EffectCatalog")]
    public sealed class EffectCatalog : ScriptableObject, IEffectCatalog
    {
        [SerializeField] private List<EffectPreset> presets = new List<EffectPreset>();

        private readonly Dictionary<string, EffectPreset> presetsByKey = new Dictionary<string, EffectPreset>();
        private bool isCacheDirty = true;

        public IReadOnlyList<EffectPreset> Presets => presets;

        public bool TryGet(string key, out EffectPreset preset)
        {
            EnsureCache();
            return presetsByKey.TryGetValue(key, out preset);
        }

        private void OnValidate()
        {
            isCacheDirty = true;
        }

        private void EnsureCache()
        {
            if (!isCacheDirty)
                return;

            presetsByKey.Clear();
            foreach (EffectPreset preset in presets)
            {
                if (preset == null || string.IsNullOrWhiteSpace(preset.Key))
                    continue;

                if (presetsByKey.ContainsKey(preset.Key))
                {
                    Debug.LogWarning($"[EffectCatalog] Duplicate effect key '{preset.Key}' in '{name}'. First preset wins.", this);
                    continue;
                }

                presetsByKey.Add(preset.Key, preset);
            }

            isCacheDirty = false;
        }
    }
}
