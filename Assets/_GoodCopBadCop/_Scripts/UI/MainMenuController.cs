using System;
using System.Collections;
using DG.Tweening;
using Unity.VisualScripting;
using UnityEngine;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private GameObject mainMenu;
    [SerializeField] private Animator rollingShutter;
    [SerializeField] private GameObject[] chairs;
    [SerializeField] private GameObject _camera;
    [SerializeField] private Transform _camEndPos;
    [SerializeField] WindowLampController windowLampController;
    [SerializeField] private float _timeTillOpenWindow = 8;

    private void Start()
    {
        UIController.Instance.ClosePlayerUI();
        
        _camera.transform.DOMove(_camEndPos.position, 30);
        StartCoroutine(WaitAndOpenWindow());
    }

    IEnumerator WaitAndOpenWindow()
    {
        yield return new WaitForSeconds(_timeTillOpenWindow);
        GameManager.Instance.OpenWindow();
    }
    

    public void StartGame()
    {
        foreach (var chair in chairs)
        {
            chair.SetActive(false);
        }
        mainMenu.SetActive(false);
    }
    
    
}
