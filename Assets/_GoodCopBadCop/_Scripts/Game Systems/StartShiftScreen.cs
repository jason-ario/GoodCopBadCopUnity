using System;
using System.Collections;
using UnityEngine;

public class StartShiftScreen : MonoBehaviour
{
    [Header("Day Number Text")]
    [SerializeField] private TMPTextReveal dayNumberText; 
    [SerializeField] float dayNumberDelay = 2f;
    [SerializeField] float dayNumberDuration = 4f;
    
    public void ShowDayNumber(int dayNumber)
    {
        gameObject.SetActive(true);
        StartCoroutine(StartShift(dayNumber));
    }

    IEnumerator StartShift(int dayNumber = 1)
    {
        yield return new WaitForSeconds(dayNumberDelay);
        dayNumberText.RevealText("Day " + dayNumber);
        yield return new WaitForSeconds(dayNumberDuration);
        dayNumberText.gameObject.SetActive(false);
    }
    
}
