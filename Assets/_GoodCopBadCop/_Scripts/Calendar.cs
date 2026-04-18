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
        string month = ShiftManager.Instance.CurrentDate.Substring(0, 2);
        string day = ShiftManager.Instance.CurrentDate.Substring(3, 2);
        string year = ShiftManager.Instance.CurrentDate.Substring(6, 4);
        SetDate(month, day, year);
    }

    public void SetDate(string month, string day, string year)
    {
        monthText.text = month.ToString();
        dayText.text = day.ToString();
        yearText.text = year.ToString();
    }
}
