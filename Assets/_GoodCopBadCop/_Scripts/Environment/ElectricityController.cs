using System;
using System.Collections;
using UnityEngine;

public class ElectricityController : MonoBehaviour
{
    [SerializeField] ElectricObject[] electricObjects;
    [SerializeField] private AudioClip powerOffSound;
    [SerializeField] private AudioClip powerOnSound;
    [SerializeField] AudioSource sfxSource;

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

    }
}
