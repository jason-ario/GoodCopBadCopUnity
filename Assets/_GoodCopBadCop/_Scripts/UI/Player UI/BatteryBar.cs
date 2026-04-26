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

    public void Show()
    {
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
