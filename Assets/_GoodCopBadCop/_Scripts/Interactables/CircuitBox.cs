using UnityEngine;

public class CircuitBox : Interactable
{
    [SerializeField] Animator animator;
    bool isOpened = false;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] AudioClip circuitBoxOpenSound;
    [SerializeField] AudioClip circuitBoxCloseSound;
    [SerializeField] private ElectricityController electricityController;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player); 
        ToggleCircuitBox();
    }
    
    void ToggleCircuitBox()
    {
        if (electricityController.IsPowerOn == false)
        {
            electricityController.PowerOn();
        }
        
        isOpened = !isOpened;
        if (isOpened)
        {
            audioSource.PlayOneShot(circuitBoxOpenSound);
            animator.SetBool("Open", true);
        }
        else
        {
            audioSource.PlayOneShot(circuitBoxCloseSound);
            animator.SetBool("Open", false);
        }
    }
}
