using System.Collections;
using UnityEngine;

public class ShiftManager : MonoBehaviour
{
    [SerializeField] StartShiftScreen _startShiftScreen;
    [SerializeField] FaxMachine _faxMachine;
    [SerializeField] float faxMachineDelay = 4f;
    [SerializeField] private AudioClip bellSound;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartCoroutine(StartShiftSequence());
    }

    IEnumerator StartShiftSequence()
    {
        SFXController.Instance.Play(bellSound);
        _startShiftScreen.ShowDayNumber(1);
        yield return new WaitForSeconds(faxMachineDelay);
        _faxMachine.OnShiftStart();
    }
}
