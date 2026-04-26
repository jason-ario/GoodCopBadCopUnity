using System;
using System.Collections;
using UnityEngine;
using Random = System.Random;

public class ElectricityController : MonoBehaviour
{
    [SerializeField] ElectricObject[] electricObjects;
    [SerializeField] private AudioClip powerOffSound;
    [SerializeField] private AudioClip powerOnSound;
    [SerializeField] AudioSource sfxSource;
    [SerializeField] private Vector2 powerOutageRandomTime = new Vector2(60,120);
    
    void Start()
    {
        ShiftManager.Instance.OnShiftStart += StartCountdown;
    }

    void StartCountdown()
    {
        StartCoroutine(WaitAndShutDown());
    }

    IEnumerator WaitAndShutDown()
    {
        yield return new WaitForSeconds(UnityEngine.Random.Range(powerOutageRandomTime.x, powerOutageRandomTime.y));
        PowerOff();
    }
    
    [ContextMenu("Power Off")]
    public void PowerOff()
    {
        StartCoroutine(PowerOffCoroutine());
    }

    IEnumerator PowerOffCoroutine()
    {
        sfxSource.PlayOneShot(powerOffSound);

        yield return new WaitForSeconds(2f);
        foreach (var electricObject in electricObjects)
        {
            electricObject.OnElectricityTurnOff?.Invoke();
        }
        
    }
    [ContextMenu("Power On")]
    public void PowerOn()
    {
        foreach (var electricObject in electricObjects)
        {
            electricObject.OnElectricityTurnOn?.Invoke();
        }
        
        sfxSource.PlayOneShot(powerOnSound);
        
        StartCoroutine(WaitAndShutDown());
    }
}
