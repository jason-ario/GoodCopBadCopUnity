using System;
using TMPro;
using UnityEngine;

public class MoneyText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI moneyText;

    private void OnEnable()
    {
        UpdateText();
    }

    void UpdateText()
    {
        moneyText.text = GlobalHostVariables.Instance.money.Value.ToString();
    }
}
