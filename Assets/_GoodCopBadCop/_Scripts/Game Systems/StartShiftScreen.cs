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
    private Coroutine _routine;

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

        // Restart cleanly if a previous reveal is still running (no overlapping coroutines).
        if (_routine != null)
            StopCoroutine(_routine);

        // Hide the HUD for the whole reveal. UIController defers any ShowPlayerUI() made by
        // the day-start path until EndDayNumberHudHide, so this is consistent on every path.
        if (UIController.Instance != null)
            UIController.Instance.BeginDayNumberHudHide();

        _routine = StartCoroutine(StartShift(dayNumber));
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

        _routine = null;
        EndHudHide();
    }

    private void OnDisable()
    {
        // Disabling the object kills the coroutine — never leave the HUD stuck hidden.
        if (_routine == null) return;
        _routine = null;
        EndHudHide();
    }

    private static void EndHudHide()
    {
        if (UIController.Instance != null)
            UIController.Instance.EndDayNumberHudHide();
    }
    
}
