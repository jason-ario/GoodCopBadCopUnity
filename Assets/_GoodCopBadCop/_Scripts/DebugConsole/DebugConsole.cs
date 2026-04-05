using System;
using Unity.Netcode;
using UnityEngine;

public class DebugConsole : MonoBehaviour
{
    public bool skipMainMenu; 
    public bool skipLobby;
    public bool skipInitialShiftTransition;
    public bool cutsceneMode;
    [SerializeField] private MainMenuController _mainMenuController;
    [SerializeField] private GameObject mainMenuScreen;

    public static DebugConsole Instance;
    
    void Awake()
    {
        Instance = this;
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
            LobbyManager.Instance.CreateLobby();
            GameManager.Instance.TryStartGame(true);
        }
        
        if (cutsceneMode)
        {
            UIController.Instance.ClosePlayerUI();
        }
        
        if (skipLobby)
        {
            ShiftManager.Instance.StartNewShift();
        }

    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            GlobalHostVariables.Instance.AddMoney(1000);
        }
    }
}
