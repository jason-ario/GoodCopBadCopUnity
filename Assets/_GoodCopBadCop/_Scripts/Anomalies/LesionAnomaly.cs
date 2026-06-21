using System.Collections;
using UnityEngine;

/// <summary>
/// Gradually fades a lesion texture overlay across one or more character renderers using
/// the Character shader's TCP2_LESION keyword and _LesionStrength property. Supports
/// multiple renderers (e.g. head, body, hands) so lesions can span the full character.
/// Each renderer gets its own material instance to avoid affecting other characters.
/// </summary>
public class LesionAnomaly : PhysicalAnomaly
{
    private const string LesionKeyword = "TCP2_LESION";
    private static readonly int LesionStrengthId = Shader.PropertyToID("_LesionStrength");

    [SerializeField] private Renderer[] renderers;
    [SerializeField] private float fadeDuration = 2.5f;

    private Material[] _materialInstances;
    private MaterialPropertyBlock _propertyBlock;
    private Coroutine _activeCoroutine;

    private void Awake()
    {
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"[LesionAnomaly] No renderers assigned on '{gameObject.name}'. Anomaly will not function.", this);
            return;
        }

        // Create per-instance materials upfront so keyword toggling doesn't affect shared assets.
        _materialInstances = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                _materialInstances[i] = renderers[i].material;
            else
                Debug.LogWarning($"[LesionAnomaly] Renderer at index {i} is null on '{gameObject.name}'.", this);
        }

        _propertyBlock = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Immediately sets the lesion strength to 0 and disables the keyword without any fade.
    /// Call this on anomalies that were not selected to ensure the shader is in a clean state.
    /// </summary>
    public override void InitializeDisabled()
    {
        if (_materialInstances == null) return;

        ApplyStrengthToAll(0f);

        foreach (Material mat in _materialInstances)
            mat?.DisableKeyword(LesionKeyword);
    }

    /// <summary>Enables the lesion keyword on all renderers and fades strength from 0 to 1.</summary>
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_materialInstances == null) return;

        foreach (Material mat in _materialInstances)
            mat?.EnableKeyword(LesionKeyword);

        StartFade(0f, 1f);
    }

    /// <summary>Fades strength back to 0 then disables the lesion keyword on all renderers.</summary>
    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();

        if (_materialInstances == null) return;

        StartFade(1f, 0f, onComplete: () =>
        {
            foreach (Material mat in _materialInstances)
                mat?.DisableKeyword(LesionKeyword);
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

            ApplyStrengthToAll(strength);

            yield return null;
        }

        // Ensure we land exactly on the target value.
        ApplyStrengthToAll(to);
        _activeCoroutine = null;

        onComplete?.Invoke();
    }

    /// <summary>Writes _LesionStrength to every renderer via a shared MaterialPropertyBlock.</summary>
    private void ApplyStrengthToAll(float strength)
    {
        _propertyBlock.SetFloat(LesionStrengthId, strength);

        foreach (Renderer r in renderers)
            r?.SetPropertyBlock(_propertyBlock);
    }
}
