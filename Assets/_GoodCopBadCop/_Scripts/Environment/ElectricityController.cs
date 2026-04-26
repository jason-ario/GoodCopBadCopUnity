using System;
using UnityEngine;

public class ElectricityController : MonoBehaviour
{
    [SerializeField] ElectricObject[] electricObjects;

    [ContextMenu("Power Off")]
    public void PowerOff()
    {
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
    }
}
