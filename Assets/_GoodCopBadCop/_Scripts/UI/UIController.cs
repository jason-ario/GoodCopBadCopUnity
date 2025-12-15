using System;
using UnityEngine;

public class UIController : MonoBehaviour
{
    public static UIController Instance;

    [SerializeField] private GameObject levelSelectUI;
    [SerializeField] private GameObject playerUI;
    [SerializeField] private GameObject _textChat;
    [SerializeField] private GameObject toolShopUI;
    [SerializeField] private GameObject caseFileUI;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _textChat.SetActive(false);
        playerUI.SetActive(true);
    }

    private void Update()
    {
        if (playerUI.activeSelf == false)
        {
            return;
        }
        
        if(PlayerInstance.Instance.CanControl == false) return;
        
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            ToggleChatUI();
        }
    }
    
    public void ToggleChatUI()
    {
        _textChat.SetActive(!_textChat.activeSelf);

        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(!_textChat.activeSelf);
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

    public void OpenToolShopUI()
    {
        toolShopUI.SetActive(true);
        playerUI.SetActive(false);
        
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(false);
    }
    
    public void CloseToolShopUI()
    {
        toolShopUI.SetActive(false);
        playerUI.SetActive(true);
        
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
    }
}
