using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class MoneyText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI moneyText;

    IEnumerator Start()
    {
        while (GlobalHostVariables.Instance == null)
        {
            yield return new WaitForEndOfFrame();
        }
        
        GlobalHostVariables.Instance.money.OnValueChanged += UpdateText;
        UpdateText(GlobalHostVariables.Instance.money.Value, GlobalHostVariables.Instance.money.Value);
    }
    
    void OnEnable()
    {
        if (GlobalHostVariables.Instance != null)
        {
            GlobalHostVariables.Instance.money.OnValueChanged += UpdateText;
        }
    }
    
    void OnDisable()
    {
        GlobalHostVariables.Instance.money.OnValueChanged -= UpdateText;
    }

    private void UpdateText(int previousValue, int newValue)
    {
        Debug.Log("Should Update Money");
        moneyText.text = GlobalHostVariables.Instance.money.Value.ToString();    
    }
}
