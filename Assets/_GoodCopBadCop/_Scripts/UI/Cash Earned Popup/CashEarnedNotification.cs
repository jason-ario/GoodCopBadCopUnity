using TMPro;
using UnityEngine;

public class CashEarnedNotification : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI mainText;
    [SerializeField] private TextMeshProUGUI couponAmountText;
    [SerializeField] private TextMeshProUGUI couponLabelText;
    [SerializeField] Color positiveColor;
    [SerializeField] Color negativeColor;

    public void Initialize(int cashAmount, string message)
    {
        mainText.text = message;
        couponAmountText.text = cashAmount.ToString();
        
        if (cashAmount > 0)
        {
            mainText.color = Color.white;
            couponLabelText.color = positiveColor;
        }
        else
        {
            mainText.color = negativeColor;
            couponLabelText.color = negativeColor;
        }
    }
}
