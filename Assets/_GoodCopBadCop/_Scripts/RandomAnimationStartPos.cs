using UnityEngine;

public class RandomAnimationStartPos : MonoBehaviour
{
    private Animator _animator;

    void Start()
    {
        _animator = GetComponent<Animator>();

        AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
        _animator.Play(stateInfo.fullPathHash, 0, Random.value);
    }
}
