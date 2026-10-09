using UnityEngine;

public class Envelope : PickableObject
{
    [SerializeField] private Animator _animator;
    
    /// <summary>LMB opens the envelope.</summary>
    public override string GetHeldUseVerb() => "Open";

    public override void OnStartUse()
    {
        _animator.SetBool("Open", true);
    }
}
