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
    public ScreenDamageCanvas ScreenDamageCanvas => _screenDamageCanvas;
    public bool IsPaused => pauseMenuOpened;

    [SerializeField] private AudioClip transitionToGameplayStinger;
    [SerializeField] private CouponUIController couponUIController;
    [SerializeField] private GameObject pauseMenu;
    private bool pauseMenuOpened = false;
    
    bool showedCursorBeforePaused = false;
    bool couldControlBeforePaused = false;
    bool couldLookBeforePaused = false;
    bool showedReticleBeforePause = false;

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
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().LookAtTarget(toolShopLookTarget);
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
        endOfShiftReportUI.PlayReport(reportRowDatas);
    }

    public void HideEndOfShiftReport()
    {
        endOfShiftReportUI.gameObject.SetActive(false);
    }

    public void OpenStartShiftScreen()
    {
        startShiftScreen.SetActive(true);
        PlayerInstance.Instance.OpenedUIPanel();
    }
    
    public void CloseStartShiftScreen()
    {
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

    public void PlayEarnedCashUIAnimation(int cashAmount)
    {
        couponUIController.PlayCashAnimation(cashAmount);
    }

    
    public void OpenPauseMenu()
    {
        pauseMenuOpened = true;
        couldControlBeforePaused = PlayerInstance.Instance.CanControl;
        couldLookBeforePaused = PlayerInstance.Instance.GetComponent<PlayerMovementController>().CanLook;
        showedCursorBeforePaused = Cursor.visible;

        showedReticleBeforePause = PlayerInstance.Instance.PlayerInteractionController.ReticleActive;
        
        ShowCursor();
        
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(false);
        PlayerInstance.Instance.CanControl = false;
        pauseMenu.SetActive(true);
    }
    
    public void ClosePauseMenu()
    {
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(couldControlBeforePaused);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanLook(couldLookBeforePaused);
        
        if (showedCursorBeforePaused == false)
        {
            HideCursor();
        }
        
        if (showedReticleBeforePause == false)
        {
            PlayerInstance.Instance.PlayerInteractionController.SetReticleActive(true);
        }
        
        pauseMenuOpened = false;
        pauseMenu.SetActive(false);
    }
}
