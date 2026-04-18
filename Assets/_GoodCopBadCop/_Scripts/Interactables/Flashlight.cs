using Unity.Netcode;
using UnityEngine;

public class Flashlight : PickableObject
{
    [SerializeField] GameObject flashlightLight;
    [SerializeField] AudioClip flashlightOnClip;
    [SerializeField] AudioClip flashlightOffClip;
    private NetworkVariable<bool> _isOn = new NetworkVariable<bool>(false);
    [SerializeField] private AudioSource audioSource;
    
    public override void OnStartUse()
    {
        base.OnStartUse();
        //playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);
        ToggleFlashlight();
    }
    
    void ToggleFlashlight()
    {
        _isOn.Value = !_isOn.Value;
        flashlightLight.SetActive(_isOn.Value);
        audioSource.PlayOneShot(_isOn.Value ? flashlightOnClip : flashlightOffClip);
    }
}
