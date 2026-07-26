using System;
using System.Collections;
using UnityEngine;

public class StartShiftScreen : MonoBehaviour
{
    private static readonly int BlackBarsOn = Animator.StringToHash("BlackBarsOn");

    [Header("Day Number Text")]
    [SerializeField] private TMPTextReveal dayNumberText; 
    [SerializeField] float dayNumberDelay = 2f;
    [SerializeField] float dayNumberDuration = 4f;

    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
    }

    public void ShowDayNumber(int dayNumber)
    {
        // Re-enable the text object — it is disabled at the end of each play so it
        // must be explicitly re-activated before starting the reveal coroutine again.
        dayNumberText.gameObject.SetActive(true);
        gameObject.SetActive(true);
        StartCoroutine(StartShift(dayNumber));
    }

    IEnumerator StartShift(int dayNumber = 1)
    {
        if (_animator != null)
            _animator.SetBool(BlackBarsOn, true);

        yield return new WaitForSeconds(dayNumberDelay);
        dayNumberText.RevealText("Day " + dayNumber);
        yield return new WaitForSeconds(dayNumberDuration);
        dayNumberText.gameObject.SetActive(false);

        if (_animator != null)
            _animator.SetBool(BlackBarsOn, false);
    }
    
}
