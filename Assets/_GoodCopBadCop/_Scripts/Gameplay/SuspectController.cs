using System;
using DG.Tweening;
using FIMSpace.FLook;
using UnityEngine;

public class SuspectController : MonoBehaviour
{
    public static SuspectController Instance;
    [SerializeField] private FLookAnimator _lookAnimator;
    [TextArea(5,10)]
    [SerializeField] string prompt;

    [SerializeField] private GameObject llmChatController;
    [SerializeField] private SuspectData suspectData;
    [SerializeField] private Transform spawnPos;
    [SerializeField] private Transform standPos;
    [SerializeField] Animator animator;
    [SerializeField] private Transform suspectTransform;

    public void EnableLook()
    {
        _lookAnimator.ObjectToFollow = Camera.main.transform;
    }

    private void Start()
    {
        GameManager.Instance.OnRoundStart += StartRound;
        suspectTransform.rotation = spawnPos.rotation;
        suspectTransform.position = spawnPos.position;
    }

    void StartRound()
    {
        animator.SetBool("Walking", true);
        suspectTransform.DOMove(standPos.position, 3f).OnComplete(ArrivedAtPosition);
    }

    void ArrivedAtPosition()
    {
        suspectTransform.DORotateQuaternion(standPos.rotation, 1f);
        animator.SetBool("Walking", false);
    }
}

