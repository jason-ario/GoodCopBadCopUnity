using UnityEngine;

public class UIWobble : MonoBehaviour
{
    [SerializeField] private TMPWobbleProfile wobbleProfile;
    
    private RectTransform rectTransform;
    private Vector3 originalPosition;
    private float elapsedTime;
    private float randomPhase;

    private void Start()
    {
        rectTransform = GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            originalPosition = rectTransform.anchoredPosition;
        }
        
        if (wobbleProfile != null)
        {
            randomPhase = Random.Range(wobbleProfile.randomPhaseMin, wobbleProfile.randomPhaseMax);
        }
    }

    private void Update()
    {
        if (rectTransform != null && wobbleProfile != null)
        {
            elapsedTime += Time.deltaTime;
            ApplyShake();
        }
    }

    private void ApplyShake()
    {
        float time = elapsedTime * wobbleProfile.speed + randomPhase;
        
        float offsetX = Mathf.Sin(time * wobbleProfile.xFrequencyMultiplier) * wobbleProfile.amountX;
        float offsetY = Mathf.Cos(time * wobbleProfile.yFrequencyMultiplier) * wobbleProfile.amountY;
        
        // Add noise
        offsetX += Random.Range(-wobbleProfile.noiseAmount, wobbleProfile.noiseAmount);
        offsetY += Random.Range(-wobbleProfile.noiseAmount, wobbleProfile.noiseAmount);
        
        rectTransform.anchoredPosition = originalPosition + new Vector3(offsetX, offsetY, 0);
    }

    public void SetWobbleProfile(TMPWobbleProfile profile)
    {
        wobbleProfile = profile;
        elapsedTime = 0;
        randomPhase = wobbleProfile != null 
            ? Random.Range(wobbleProfile.randomPhaseMin, wobbleProfile.randomPhaseMax) 
            : 0;
    }
}
