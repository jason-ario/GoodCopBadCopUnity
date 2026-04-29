using System.Collections;
using UnityEngine;

public class LogoMaterialController : MonoBehaviour
{
    private static readonly int BurnProgress = Shader.PropertyToID("_BurnProgress");

    [SerializeField] private Material _logoMaterial;
    [SerializeField] private float _delay = 1f;
    [SerializeField] private float _duration = 2f;
    [SerializeField] private float _targetBurnProgress = 0.23f;
    [SerializeField] AudioClip _burnSound;

    private void Start()
    {
        StartCoroutine(AnimateBurnProgress());
    }

    /// <summary>
    /// Waits for the configured delay, then animates _BurnProgress from 1 to the target value.
    /// </summary>
    private IEnumerator AnimateBurnProgress()
    {
        _logoMaterial.SetFloat(BurnProgress, 1f);

        yield return new WaitForSeconds(_delay); 

        float elapsed = 0f;
        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _duration);
            _logoMaterial.SetFloat(BurnProgress, Mathf.Lerp(1f, _targetBurnProgress, t));
            yield return null;
        }
        SFXController.Instance.Play(_burnSound);

        _logoMaterial.SetFloat(BurnProgress, _targetBurnProgress);
    }
}
