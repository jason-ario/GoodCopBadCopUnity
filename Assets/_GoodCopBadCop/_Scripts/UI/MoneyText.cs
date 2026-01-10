using System;
using TMPro;
using UnityEngine;

public class MoneyText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI moneyText;

    private void Start()
    {
        UpdateText();
    }
    
    void OnEnable()
    {
        UpdateText();
    }

    void UpdateText()
    {
        if (GlobalHostVariables.Instance == null) return;
        
        moneyText.text = "$" + GlobalHostVariables.Instance.money.Value.ToString();
    }
}
