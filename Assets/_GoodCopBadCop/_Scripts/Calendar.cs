using System;
using TMPro;
using UnityEngine;

public class Calendar : MonoBehaviour
{
    [SerializeField] TextMeshPro monthText;
    [SerializeField] TextMeshPro dayText;
    [SerializeField] TextMeshPro yearText;

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += OnShiftStart;
    }

    void OnShiftStart()
    {
        SetDate(ShiftManager.Instance.currentMonth, ShiftManager.Instance.currentDay, ShiftManager.Instance.currentYear);
    }

    public void SetDate(string month, string day, string year)
    {
        monthText.text = month.ToString();
        dayText.text = day.ToString();
        yearText.text = year.ToString();
    }
}
