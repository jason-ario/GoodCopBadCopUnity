using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

public class FaxMachine : MonoBehaviour
{
    [SerializeField] private GameObject paper;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private MachineShake machineShake;
    [SerializeField] private Animator faxMachineAnimator;
    
    private void Start()
    {
        GameManager.Instance.OnGameStart += OnGameStart;
    }

    private void OnGameStart()
    {

        StartCoroutine(RunFaxMachine());
    }

    IEnumerator RunFaxMachine()
    {
        _audioSource.Play();
        machineShake.enabled = true;
        yield return new WaitForSeconds(10.5f);
        paper.gameObject.SetActive(true);
        faxMachineAnimator.SetBool("On", true);
        yield return new WaitForSeconds(3);
        machineShake.enabled = false;
    }
}
