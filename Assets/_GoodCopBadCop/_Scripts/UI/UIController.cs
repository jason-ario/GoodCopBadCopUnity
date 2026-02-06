using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UIController : MonoBehaviour
{
    public static UIController Instance;

    [SerializeField] private GameObject levelSelectUI;
    [SerializeField] private GameObject playerUI;
    [SerializeField] private GameObject toolShopUI;
    [SerializeField] private Animator screenFade;
    [SerializeField] private Animator newspaper;
    [SerializeField] private Button backButton;
    [SerializeField] private ScreenDamageCanvas _screenDamageCanvas; 
    
    public ScreenDamageCanvas ScreenDamageCanvas => _screenDamageCanvas;

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
    
    public void OpenNewspaper()
    {
        newspaper.SetBool("Open", true);
    }
    
    public void CloseNewspaper()
    {
        newspaper.SetBool("Open", false);
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


}
