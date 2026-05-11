using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Light))]
public class BlinkLight : MonoBehaviour
{
    private const float BlinkInterval = 2f;

    private Light _light;

    void Start()
    {
        _light = GetComponent<Light>();
        StartCoroutine(BlinkRoutine());
    }

    /// <summary>
    /// Toggles the light on and off every <see cref="BlinkInterval"/> seconds.
    /// </summary>
    private IEnumerator BlinkRoutine()
    {
        while (true)
        {
            _light.enabled = !_light.enabled;
            yield return new WaitForSeconds(BlinkInterval);
        }
    }
}
