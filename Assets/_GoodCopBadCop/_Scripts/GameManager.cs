using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;
    [SerializeField] private Animator rollingShutter;

    public UnityAction OnRoundStart;
    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        StartCoroutine(StartLevel());
    }

    IEnumerator StartLevel()
    {
        yield return new WaitForSeconds(3);
        OnRoundStart?.Invoke();
        rollingShutter.SetBool("Open", true);
    }
}
