using System;
using Unity.Netcode;
using UnityEngine;

public class DebugConsole : MonoBehaviour
{
    public bool skipMainMenu; 
    public bool cutsceneMode;
    [SerializeField] private MainMenuController _mainMenuController;
    [SerializeField] private GameObject mainMenuScreen;

    void Awake()
    {
        if (cutsceneMode)
        {
            _mainMenuController.enabled = false;
            mainMenuScreen.SetActive(false);
        }
    }
    
    private void Start()
    {
        if (skipMainMenu)
        {
            if (cutsceneMode)
            {
                UIController.Instance.ClosePlayerUI();
                return;
            }
            NetworkManager.Singleton.StartHost();
            GameManager.Instance.TryStartGame(true);
        }
        
        if (cutsceneMode)
        {
            UIController.Instance.ClosePlayerUI();
        }

    }
}
