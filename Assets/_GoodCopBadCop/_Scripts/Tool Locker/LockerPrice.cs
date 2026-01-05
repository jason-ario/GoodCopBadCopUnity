using System;
using TMPro;
using UnityEngine;

public class LockerPrice : MonoBehaviour
{
    [SerializeField] private TextMeshPro _textMeshPro;
    private Animator _animator;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _animator.keepAnimatorStateOnDisable = true;

    }

    private void OnEnable()
    {
    }

    public void SetPrice(int price)
    {
        _textMeshPro.text = "$" + price.ToString();
    }
}
