using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Steamworks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public static UIController Instance;

    /// <summary>Fired when the End of Shift Report becomes visible.</summary>
    public Action OnReportShown { get; set; }

    /// <summary>Fired when the End of Shift Report is hidden.</summary>
    public Action OnReportHidden { get; set; }

    /// <summary>Fired on the local client whenever any player opens the tool shop.</summary>
    public static event Action OnToolShopOpened;

    [SerializeField] RawImage cameraImage;
    [SerializeField] private GameObject levelSelectUI;
    [SerializeField] private GameObject playerUI;
    [SerializeField] private GameObject toolShopUI;
    [SerializeField] private Animator screenFade;
    [SerializeField] private Animator newspaper;
    [SerializeField] private Button backButton;
    [SerializeField] private ScreenDamageCanvas _screenDamageCanvas;
    [SerializeField] private EndOfShiftReportUI endOfShiftReportUI;
    [SerializeField] private GameObject startShiftScreen;
    [SerializeField] private GameObject inviteFriendsPanel;
    [SerializeField] private CashNotificationPopupManager cashNotificationPopupManager;
    public ScreenDamageCanvas ScreenDamageCanvas => _screenDamageCanvas;
    public bool IsPaused => pauseMenuOpened;

    [SerializeField] private AudioClip transitionToGameplayStinger;
    [SerializeField] private GameObject pauseMenu;
    private bool pauseMenuOpened = false;
    
    bool showedCursorBeforePaused = false;
    bool couldControlBeforePaused = false;
    bool couldLookBeforePaused = false;
    bool showedReticleBeforePause = false;
    bool playerUIWasActiveBeforePaused = false;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (playerUI.activeSelf == false)
        {
            return;
        }
        
        if(PlayerInstance.Instance == null)
        {
            return;
        }

        if (backButton.gameObject.activeSelf == true)
        {
            if(Input.GetButtonDown("Back"))
            {
                backButton.onClick.Invoke();
            }
        }

        if (Input.GetButtonDown("Pause"))
        {
            if (pauseMenuOpened)
            {
                ClosePauseMenu();
            }
            else
            {
                OpenPauseMenu();
            }
        }
        
        if(PlayerInstance.Instance.CanControl == false) return;
    }
    
    public void OpenLevelSelectUI()
    {
        levelSelectUI.SetActive(true);
        playerUI.SetActive(false);
    }
    
    public void CloseLevelSelectUI()
    {
        levelSelectUI.SetActive(false);
        playerUI.SetActive(true);
    }
    
    public void OpenToolShop(Transform toolShopLookTarget)
    {
        PlayerInstance.Instance.SetCanInteract(false);
        ShowCursor();
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(toolShopLookTarget);
        OnToolShopOpened?.Invoke();
        StartCoroutine(WaitAndOpenShopUI());
    }

    IEnumerator WaitAndOpenShopUI()
    {
        yield return new WaitForSeconds(.5f);
        playerUI.SetActive(false);
        toolShopUI.SetActive(true);
    }
    
    public void CloseToolShopUI()
    {
        toolShopUI.SetActive(false);
        playerUI.SetActive(true);
        PlayerInstance.Instance.SetCanInteract(true);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }

    public void ClosePlayerUI()
    {
        playerUI.SetActive(false);
    }
    
    public void ShowPlayerUI()
    {
        playerUI.SetActive(true);
    }
    
    public void FadeIn()
    { 
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();

        foreach (var canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

        }
        
        screenFade.SetBool("Black", true);
    }
    
    public void FadeOut()
    {
        screenFade.SetBool("Black", false);
        
        CanvasGroup[] canvasGroups = MainMenuController.Instance.GetComponentsInChildren<CanvasGroup>();

        foreach (var canvasGroup in canvasGroups)
        {
            canvasGroup.interactable = true;
        }
    }
    

    public void ShowBackButton(UnityAction onClickCallback)
    {
        backButton.onClick.AddListener(onClickCallback);
        backButton.gameObject.SetActive(true);
    }

    public void HideBackButton()
    {
        backButton.onClick.RemoveAllListeners();
        backButton.gameObject.SetActive(false);
    }


    public void ShowEndShiftReport(List<EndOfShiftReportUI.ReportRowData> reportRowDatas)
    {
        ShowCursor();
        endOfShiftReportUI.PlayReport(reportRowDatas);
        OnReportShown?.Invoke();
    }

    public void HideEndOfShiftReport()
    {
        endOfShiftReportUI.gameObject.SetActive(false);
        OnReportHidden?.Invoke();
    }

    public void OpenStartShiftScreen()
    {
        UIController.Instance.ShowCursor();
        startShiftScreen.SetActive(true);
        PlayerInstance.Instance.OpenedUIPanel();
    }
    
    public void CloseStartShiftScreen()
    {
        UIController.Instance.HideCursor();
        startShiftScreen.SetActive(false);
        PlayerInstance.Instance.ClosedUIPanel();
    }

    public void EnterFirstShift()
    {
        CloseStartShiftScreen();
        SFXController.Instance.Play(transitionToGameplayStinger);
        ShiftManager.Instance.InitiateIntroCutscene();
    }

    public void OpenInvitePanel()
    {
        PlayerInstance.Instance.OpenedUIPanel();
        LobbyManager.Instance.OpenInviteFriendsPopup();
        inviteFriendsPanel.SetActive(true);
    }

    public void CloseInviteFriendsScreen()
    {
        PlayerInstance.Instance.ClosedUIPanel();
        inviteFriendsPanel.SetActive(false);
    }

    public void ShowCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.Confined;
    }

    public void HideCursor()
    {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    public RawImage GetCameraImage()
    {
        return cameraImage;
    }

    public void OpenPauseMenu()
    {
        pauseMenuOpened = true;
        couldControlBeforePaused = PlayerInstance.Instance.CanControl;
        couldLookBeforePaused = PlayerInstance.Instance.GetComponent<PlayerMovementController>().CanLook;
        showedCursorBeforePaused = Cursor.visible;

        // Capture reticle state before we disable control (which will deactivate it).
        // Use couldControlBeforePaused && couldLookBeforePaused as the source of truth,
        // since the reticleActive flag can be stale when the CanControl setter skips
        // SetReticleActive due to CanLook being false.
        showedReticleBeforePause = couldControlBeforePaused && couldLookBeforePaused;

        playerUIWasActiveBeforePaused = playerUI.activeSelf;
        playerUI.SetActive(false);
        
        ShowCursor();
        
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(false);
        PlayerInstance.Instance.CanControl = false;
        pauseMenu.SetActive(true);
    }
    
    public void ClosePauseMenu()
    {
        // Restore CanLook first so the CanControl setter can properly re-enable the reticle.
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(couldLookBeforePaused);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(couldControlBeforePaused);
        
        if (showedCursorBeforePaused == false)
        {
            HideCursor();
        }
        
        PlayerInstance.Instance.PlayerInteractionController.SetReticleActive(showedReticleBeforePause);

        playerUI.SetActive(playerUIWasActiveBeforePaused);

        pauseMenuOpened = false;
        pauseMenu.SetActive(false);
    }

    public void ShowCashPopUpNotification(int amount, string message)
    {
        cashNotificationPopupManager.SpawnCashNotification(amount, message);
    }
}
