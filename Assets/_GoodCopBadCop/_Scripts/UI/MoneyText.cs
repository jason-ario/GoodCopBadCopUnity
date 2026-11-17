using System;
using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class MoneyText : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI moneyText;

    [Tooltip("Keep the authored font in every language. The label only shows digits, which every font has, and a " +
             "language's UI font (e.g. Oswald for Polish) can be taller or have a different baseline, which breaks the HUD layout.")]
    [SerializeField] bool keepAuthoredFont = true;

    IEnumerator Start()
    {
        if (keepAuthoredFont && moneyText != null && moneyText.GetComponent<LocalizedText>() == null)
            LocalizationTextRuntime.KeepOwnFont(moneyText);

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
