using UnityEngine;
using UnityEngine.UI;

public class BatteryBar : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    
    public void UpdateBar(InternalBattery internalBattery)
    {
        if (internalBattery != null && fillImage != null)
        {
            fillImage.fillAmount = internalBattery.GetBatteryPercentage();
        }
    }

    /// <summary>Updates the bar fill directly from a 0–1 normalised percentage.</summary>
    public void UpdateBar(float fillPercent)
    {
        if (fillImage != null)
            fillImage.fillAmount = Mathf.Clamp01(fillPercent);
    }

    public void Show()
    {
        
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
