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

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    public void Clear()
    {
        labelText.text = " ";
        valueText.text = " ";
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
        text = text + " <sprite=0>";
        
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
    }
}