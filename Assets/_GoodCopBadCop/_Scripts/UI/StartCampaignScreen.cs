using System.Collections;
using UnityEngine;
using TMPro;

public class StartCampaignScreen : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI inviteCodeText;
    [SerializeField] private PlayerInfoPanel[] playerPanels;
    [SerializeField] private GameObject startButton;
    [SerializeField] private GameObject waitForHostText;

    private void OnEnable()
    {
        LobbyManager.Instance.OnLobbyUpdated += RefreshUI;
        LobbyManager.Instance.OnKicked += OnKicked;
        
        RefreshUI();
        //StartCoroutine(RefreshUIRepeating());
    }

    public void ExitLobby()
    {
        LobbyManager.Instance.ExitLobby();
    }

    IEnumerator RefreshUIRepeating()
    {
        while (true)
        {
            RefreshUI();
            yield return new WaitForSeconds(1f);
        }
    }
    
    private void OnDisable()
    {
        LobbyManager.Instance.OnLobbyUpdated -= RefreshUI;

    }

    public void StartCampaignAsHost()
    {
        LobbyManager.Instance.CreateLobby();
        RefreshUI();
    }

    void OnKicked()
    {
        
    }

    private void RefreshUI()
    {
        Debug.Log("Refreshing UI");
        
        var members = LobbyManager.Instance.GetMembersSnapshot();
        inviteCodeText.text = InviteCodeUtility.EncodeLobbyId(LobbyManager.Instance.CurrentLobby.Id);

        for (int i = 0; i < playerPanels.Length; i++)
        {
            playerPanels[i].gameObject.SetActive(false);
        }

        for (int i = 0; i < members.Length; i++)
        {
            playerPanels[i].PopulateInfo(members[i].Name);
            playerPanels[i].gameObject.SetActive(true);
        }

        bool isHost = LobbyManager.Instance.IsHost;

        startButton.SetActive(isHost);
        waitForHostText.SetActive(!isHost);
    }
}