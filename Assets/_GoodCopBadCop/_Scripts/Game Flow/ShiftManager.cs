using System.Collections;
using UnityEngine;

public class ShiftManager : MonoBehaviour
{
    [SerializeField] StartShiftScreen _startShiftScreen;
    [SerializeField] FaxMachine _faxMachine;
    [SerializeField] float faxMachineDelay = 4f;
    [SerializeField] private AudioClip bellSound;
    [SerializeField] private AudioClip knockOnDoorSound;
    [SerializeField] private GameObject cardboardBox;
    [SerializeField] private MachineShake doorShake;

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
        //_faxMachine.OnShiftStart();
        yield return new WaitForSeconds(10);
        GiveBonusBox();
    }

    public void GiveBonusBox()
    {
        StartCoroutine(BonusBoxSequence());
    }

    IEnumerator BonusBoxSequence()
    {
        yield return new WaitForSeconds(1f);
        SFXController.Instance.Play(knockOnDoorSound);
        doorShake.enabled = true;
        yield return new WaitForSeconds(1.5f);
        doorShake.enabled = false;
        cardboardBox.SetActive(true);
    }
}
