using UnityEngine;

public class Envelope : PickableObject
{
    [SerializeField] private Animator _animator;
    
    public override void OnStartUse()
    {
        _animator.SetBool("Open", true);
    }
}
