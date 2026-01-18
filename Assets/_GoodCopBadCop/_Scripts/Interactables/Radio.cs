using UnityEngine;

public class Radio : Interactable
{
    [SerializeField] AudioSource audioSource;
    private bool isOn;
    [SerializeField] private AudioSource _onSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        ToggleOn();
    }

    void ToggleOn()
    {
        _onSound.Play();
        if (isOn)
        {
            isOn = false;
            audioSource.Stop();
        }
        else
        {
            isOn = true;
            audioSource.Play();
        }
    }
}
