using System;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

public class EndOfShiftReportRow : MonoBehaviour
{
    [Header("Row")]
    [SerializeField] private GameObject root;

    [Header("Left Side")]
    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TMPTextReveal labelReveal;
    [SerializeField] private TMPWobbleText labelWobble;

    [Header("Right Side")]
    [SerializeField] private TextMeshProUGUI valueText;
    [SerializeField] private TMPTextReveal valueReveal;
    [SerializeField] private TMPWobbleText valueWobble;

    [Header("Coupon Icon")]
    [Tooltip("Icon that activates once the value text has finished revealing.")]
    [SerializeField] private GameObject couponIcon;

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);

        if (couponIcon != null)
            couponIcon.SetActive(false);
    }

    public void Clear()
    {
        labelText.text = " ";
        valueText.text = " ";

        if (couponIcon != null)
            couponIcon.SetActive(false);
    }

    public void Show()
    {
        if (root != null)
            root.SetActive(true);
    }

    public IEnumerator RevealLabel(string text, TMPWobbleProfile wobbleProfile)
    {
        labelText.text = "";
        
        if (labelWobble != null && wobbleProfile != null)
        {
            labelWobble.SetProfile(wobbleProfile, true);
            labelWobble.StartWobble();
        }

        if (labelReveal != null)
            yield return labelReveal.RevealText(text);
        else if (labelText != null)
            labelText.text = text;
    }

    public IEnumerator RevealValue(string text, Color color, TMPWobbleProfile wobbleProfile)
    {
        text = text;
        
        if (valueText != null)
            valueText.color = color;

        if (valueWobble != null && wobbleProfile != null)
        {
            valueWobble.SetProfile(wobbleProfile, true);
            valueWobble.StartWobble();
        }

        if (valueReveal != null)
            yield return valueReveal.RevealText(text);
        else if (valueText != null)
            valueText.text = text;

        if (couponIcon != null)
            couponIcon.SetActive(true);
    }
}