using System.Collections;
using UnityEngine;

/// <summary>
/// Gradually fades the character's eyes to pure black using the Black Eyes shader's
/// _BlackEyesStrength property. Uses a MaterialPropertyBlock to avoid material instancing,
/// allowing multiple characters to share the same material asset.
/// </summary>
public class BlackEyesAnomaly : MutationAnomaly
{
    private static readonly int BlackEyesStrengthId = Shader.PropertyToID("_BlackEyesStrength");

    [SerializeField] private Renderer headRenderer;
    [SerializeField] private float fadeDuration = 2.5f;

    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _activeCoroutine;

    private void Awake()
    {
        if (headRenderer == null)
        {
            Debug.LogWarning($"[BlackEyesAnomaly] headRenderer is not assigned on '{gameObject.name}'. Anomaly will not function.", this);
            return;
        }

        _propertyBlock = new MaterialPropertyBlock();
        headRenderer.GetPropertyBlock(_propertyBlock);
    }

    /// <summary>Fades eyes to black over fadeDuration seconds.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (headRenderer == null) return;

        StartFade(0f, 1f);
    }

    /// <summary>Fades eyes back to normal over fadeDuration seconds.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (headRenderer == null) return;

        StartFade(1f, 0f);
    }

    private void StartFade(float from, float to)
    {
        if (_activeCoroutine != null)
            StopCoroutine(_activeCoroutine);

        _activeCoroutine = StartCoroutine(AnimateBlackEyes(from, to));
    }

    private IEnumerator AnimateBlackEyes(float from, float to)
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
    }
}
