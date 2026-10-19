using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace GoodCopBadCop.Visuals
{
    /// <summary>
    /// Local-only screen-door fade for a character body. Drives the <c>_DitherFade</c> property of
    /// <c>Toony Colors Pro 2/User/Character Shader</c> (1 = opaque, 0 = invisible) on per-renderer
    /// material instances. Materials without that property are left untouched (they just pop).
    /// Material instances are created lazily on the first fade, so an unused component costs nothing.
    /// Not networked — callers run it on every peer (e.g. from a replicated NetworkVariable).
    /// </summary>
    public class CharacterDitherFade : MonoBehaviour
    {
        private static readonly int DitherFadeId = Shader.PropertyToID("_DitherFade");

        [Tooltip("Renderers to fade. Leave empty to use every MeshRenderer/SkinnedMeshRenderer under this object.")]
        [SerializeField] private Renderer[] _renderers;

        private List<Material> _materials;
        private Tween _tween;
        private float _current = 1f;

        /// <summary>Current fade value (1 = opaque, 0 = invisible).</summary>
        public float Current => _current;

        /// <summary>Sets the fade instantly, cancelling any running fade.</summary>
        public void SetFade(float value)
        {
            _tween?.Kill();
            _tween = null;
            ApplyFade(value);
        }

        /// <summary>Tweens the fade to <paramref name="target"/> over <paramref name="duration"/> seconds.</summary>
        public void FadeTo(float target, float duration, Action onComplete = null)
        {
            _tween?.Kill();

            if (duration <= 0f)
            {
                _tween = null;
                ApplyFade(target);
                onComplete?.Invoke();
                return;
            }

            _tween = DOTween.To(() => _current, ApplyFade, target, duration)
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject)
                .OnComplete(() =>
                {
                    _tween = null;
                    onComplete?.Invoke();
                });
        }

        private void ApplyFade(float value)
        {
            _current = Mathf.Clamp01(value);
            EnsureMaterials();

            for (int i = 0; i < _materials.Count; i++)
            {
                if (_materials[i] != null)
                    _materials[i].SetFloat(DitherFadeId, _current);
            }
        }

        private void EnsureMaterials()
        {
            if (_materials != null) return;
            _materials = new List<Material>();

            Renderer[] renderers = _renderers != null && _renderers.Length > 0
                ? _renderers
                : GetComponentsInChildren<Renderer>(true);

            foreach (Renderer r in renderers)
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer)) continue;

                bool hasFadeProperty = false;
                foreach (Material shared in r.sharedMaterials)
                {
                    if (shared != null && shared.HasProperty(DitherFadeId)) { hasFadeProperty = true; break; }
                }
                if (!hasFadeProperty) continue;

                foreach (Material instance in r.materials)
                {
                    if (instance == null) continue;
                    _instancedMaterials.Add(instance);
                    if (instance.HasProperty(DitherFadeId))
                        _materials.Add(instance);
                }
            }
        }

        // Every material instance created by Renderer.materials, so all of them are released.
        private readonly List<Material> _instancedMaterials = new List<Material>();

        private void OnDestroy()
        {
            _tween?.Kill();

            foreach (Material material in _instancedMaterials)
            {
                if (material != null) Destroy(material);
            }
        }
    }
}
