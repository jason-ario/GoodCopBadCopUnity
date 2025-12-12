using System;
using UnityEngine;

public class UIController : MonoBehaviour
{
    public static UIController Instance;

    [SerializeField] private GameObject levelSelectUI;
    [SerializeField] private GameObject playerUI;
    [SerializeField] private GameObject _textChat;
    
    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _textChat.SetActive(false);
    }

    private void Update()
    {
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
}
