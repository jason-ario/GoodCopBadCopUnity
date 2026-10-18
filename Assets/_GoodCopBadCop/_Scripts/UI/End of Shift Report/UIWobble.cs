using UnityEngine;

public class UIWobble : MonoBehaviour
{
    [SerializeField] private TMPWobbleProfile wobbleProfile;
    [Tooltip("Multiplier applied to the profile's offsets. 0 = no wobble, 1 = full profile strength.")]
    [SerializeField, Min(0f)] private float intensity = 1f;
    
    private RectTransform rectTransform;
    private Vector3 originalPosition;
    private bool hasOriginalPosition;
    private float elapsedTime;
    private float randomPhase;

    private void Start()
    {
        CacheOriginalPosition();
        
        if (wobbleProfile != null)
        {
            randomPhase = Random.Range(wobbleProfile.randomPhaseMin, wobbleProfile.randomPhaseMax);
        }
    }

    private void OnDisable()
    {
        ResetPosition();
    }

    private void Update()
    {
        if (rectTransform != null && wobbleProfile != null && intensity > 0f)
        {
            elapsedTime += Time.deltaTime;
            ApplyShake();
        }
    }

    private void CacheOriginalPosition()
    {
        if (hasOriginalPosition) return;

        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            originalPosition = rectTransform.anchoredPosition;
            hasOriginalPosition = true;
        }
    }

    private void ResetPosition()
    {
        if (rectTransform != null && hasOriginalPosition)
            rectTransform.anchoredPosition = originalPosition;
    }

    private void ApplyShake()
    {
        float time = elapsedTime * wobbleProfile.speed + randomPhase;
        
        float offsetX = Mathf.Sin(time * wobbleProfile.xFrequencyMultiplier) * wobbleProfile.amountX;
        float offsetY = Mathf.Cos(time * wobbleProfile.yFrequencyMultiplier) * wobbleProfile.amountY;
        
        // Add noise
        offsetX += Random.Range(-wobbleProfile.noiseAmount, wobbleProfile.noiseAmount);
        offsetY += Random.Range(-wobbleProfile.noiseAmount, wobbleProfile.noiseAmount);
        
        rectTransform.anchoredPosition = originalPosition + new Vector3(offsetX, offsetY, 0) * intensity;
    }

    public void SetWobbleProfile(TMPWobbleProfile profile)
    {
        CacheOriginalPosition();
        wobbleProfile = profile;
        elapsedTime = 0;
        randomPhase = wobbleProfile != null 
            ? Random.Range(wobbleProfile.randomPhaseMin, wobbleProfile.randomPhaseMax) 
            : 0;

        if (wobbleProfile == null)
            ResetPosition();
    }

    /// <summary>
    /// Scales the wobble strength. At 0 the element snaps back to its rest position.
    /// </summary>
    public void SetIntensity(float value)
    {
        CacheOriginalPosition();
        intensity = Mathf.Max(0f, value);

        if (intensity <= 0f)
            ResetPosition();
    }
}
