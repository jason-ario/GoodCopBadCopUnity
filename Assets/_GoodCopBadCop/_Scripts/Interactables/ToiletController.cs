using UnityEngine;

public class ToiletController : Interactable
{
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _flushSound;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        Flush();
    }

    void Flush()
    {
        _audioSource.PlayOneShot(_flushSound);
    }
}
