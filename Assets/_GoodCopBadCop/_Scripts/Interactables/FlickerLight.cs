using UnityEngine;

public class FlickerLight : MonoBehaviour
{
    [Header("Flicker Settings")]
    [SerializeField] private Light lightComponent;
    [SerializeField] private float minIntensity = 0.5f;
    [SerializeField] private float maxIntensity = 2f;
    [SerializeField] private float flickerSpeed = 0.1f;
    
    private float targetIntensity;
    private float currentIntensity;
    
    void Start()
    {
        // Get the Light component if not assigned
        if (lightComponent == null)
        {
            lightComponent = GetComponent<Light>();
        }
        
        if (lightComponent != null)
        {
            currentIntensity = lightComponent.intensity;
            targetIntensity = currentIntensity;
        }
    }

    void Update()
    {
        if (lightComponent == null) return;
        
        // Randomly change target intensity
        if (Random.value < flickerSpeed)
        {
            targetIntensity = Random.Range(minIntensity, maxIntensity);
        }
        
        // Smoothly transition to target intensity
        currentIntensity = Mathf.Lerp(currentIntensity, targetIntensity, Time.deltaTime * 10f);
        lightComponent.intensity = currentIntensity;
    }
}
