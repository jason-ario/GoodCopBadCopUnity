using System;
using TMPro;
using UnityEngine;

public class CampaignSlot : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI slotName;
    [SerializeField] private TextMeshProUGUI dayNumber;
    [SerializeField] private TextMeshProUGUI cashAmount;
    [SerializeField] Transform slotContainer;
    [SerializeField] Transform newGameContainer;

    private void Start()
    {
        newGameContainer.gameObject.SetActive(true);
        slotContainer.gameObject.SetActive(false);
    }

    public void PopulateSlotInfo(string slotName, string dayNumber, string cashAmount)
    {
        this.slotName.text = slotName;
        this.dayNumber.text = dayNumber;
        this.cashAmount.text = cashAmount;
        
        newGameContainer.gameObject.SetActive(false);
        slotContainer.gameObject.SetActive(true);
    }
}
