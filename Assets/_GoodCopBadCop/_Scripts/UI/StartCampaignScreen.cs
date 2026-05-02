using System.Collections;
using System.Threading.Tasks;
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

    /// <summary>Creates a lobby and immediately starts a single-player session without waiting for a partner.</summary>
    public async void StartSolo()
    {
        bool success = await LobbyManager.Instance.CreateLobby();
        if (success)
            GameManager.Instance.TryStartGame();
    }

    /// <summary>Creates a lobby and waits for a partner to join before allowing the host to start.</summary>
    public async void StartCampaignAsHost()
    {
        await LobbyManager.Instance.CreateLobby();
        RefreshUI();
    }

    void OnKicked()
    {
        ExitLobby();
    }

    private void RefreshUI()
    {
        Debug.Log("Refreshing UI");
        
        var members = LobbyManager.Instance.GetMembersSnapshot();
        ulong lobbyId = LobbyManager.Instance.CurrentLobby.Id;
        inviteCodeText.text = lobbyId != 0 ? lobbyId.ToString() : string.Empty;

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