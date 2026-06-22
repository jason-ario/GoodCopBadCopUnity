using System.Collections;
using UnityEngine;

/// <summary>
/// Gradually fades the character's eyes to pure black using the Character shader's
/// TCP2_BLACK_EYES keyword and _BlackEyesStrength property. The keyword is toggled on
/// a per-instance material so other characters sharing the same asset are unaffected.
/// A MaterialPropertyBlock handles the animated float to avoid redundant material duplication.
/// </summary>
public class BlackEyesAnomaly : PhysicalAnomaly
{
    private const string BlackEyesKeyword = "TCP2_BLACK_EYES";
    private static readonly int BlackEyesStrengthId = Shader.PropertyToID("_BlackEyesStrength");
    private static readonly int UseBlackEyesId = Shader.PropertyToID("_UseBlackEyes");

    [SerializeField] private Renderer headRenderer;
    [SerializeField] private float fadeDuration = 2.5f;

    private Material _materialInstance;
    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _activeCoroutine;

    private void Awake()
    {
        if (headRenderer == null)
        {
            Debug.LogWarning($"[BlackEyesAnomaly] headRenderer is not assigned on '{gameObject.name}'. Anomaly will not function.", this);
            return;
        }

        // renderer.material creates a per-instance material, needed to toggle the shader keyword
        // without affecting other characters sharing the same material asset.
        _materialInstance = headRenderer.material;
        _propertyBlock = new MaterialPropertyBlock();
        headRenderer.GetPropertyBlock(_propertyBlock);
    }

    /// <summary>
    /// Immediately sets the black eyes strength to 0 and disables the keyword without any fade.
    /// Call this on anomalies that were not selected to ensure the shader is in a clean state.
    /// </summary>
    public override void InitializeDisabled()
    {
        if (headRenderer == null) return;

        _propertyBlock.SetFloat(BlackEyesStrengthId, 0f);
        headRenderer.SetPropertyBlock(_propertyBlock);

        if (_materialInstance == null) return;
        _materialInstance.SetFloat(UseBlackEyesId, 0f);
        _materialInstance.DisableKeyword(BlackEyesKeyword);
    }

    /// <summary>Enables the black eyes keyword and fades strength from 0 to 1.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (headRenderer == null) return;

        _materialInstance.SetFloat(UseBlackEyesId, 1f);
        _materialInstance.EnableKeyword(BlackEyesKeyword);

        StartFade(0f, 1f);
    }

    /// <summary>Fades strength back to 0 and disables the black eyes keyword.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (headRenderer == null) return;

        StartFade(1f, 0f, onComplete: () =>
        {
            _materialInstance.SetFloat(UseBlackEyesId, 0f);
            _materialInstance.DisableKeyword(BlackEyesKeyword);
        });
    }

    private void StartFade(float from, float to, System.Action onComplete = null)
    {
        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);

        _activeCoroutine = StartCoroutine(AnimateStrength(from, to, onComplete));
    }

    private IEnumerator AnimateStrength(float from, float to, System.Action onComplete)
    {
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float strength = Mathf.SmoothStep(from, to, t);

            _propertyBlock.SetFloat(BlackEyesStrengthId, strength);
            headRenderer.SetPropertyBlock(_propertyBlock);

            yield return null;
        }

        // Ensure we land exactly on the target value
        _propertyBlock.SetFloat(BlackEyesStrengthId, to);
        headRenderer.SetPropertyBlock(_propertyBlock);
        _activeCoroutine = null;

        onComplete?.Invoke();
    }
}
