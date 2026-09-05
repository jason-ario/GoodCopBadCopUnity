using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the "Call in Backup" logic in the HQ Order Screen.
/// Handles money deduction and requests the local player to send a respawn RPC to the server.
/// </summary>
public class HQOrderScreen : MonoBehaviour
{
    [SerializeField] private Telephone _telephone;
    [SerializeField] private AudioSource _loopingAudio;
    [SerializeField] private Button _respawnButton;
    [SerializeField] private TextMeshProUGUI _respawnButtonText;
    
    private const int RespawnCost = 10;
    private const string RespawnTextFormat = "Call in Backup ({0} <sprite=0>)";

    private void OnEnable()
    {
        if (_loopingAudio != null)
            _loopingAudio.Play();

        if (_respawnButtonText != null)
            _respawnButtonText.text = string.Format(RespawnTextFormat, RespawnCost);

        UpdateRespawnButton();
    }

    private void Update()
    {
        UpdateRespawnButton();
    }

    private void UpdateRespawnButton()
    {
        if (_respawnButton == null || NetworkManager.Singleton == null) return;

        bool hasFunds = GlobalHostVariables.Instance != null && GlobalHostVariables.Instance.money.Value >= RespawnCost;
        bool hasTeammate = false;
        bool hasDeadTeammate = false;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<PlayerInstance>();
                if (player != null && player != PlayerInstance.Instance)
                {
                    hasTeammate = true;
                    if (player.PlayerHealth != null && player.PlayerHealth.IsDead)
                        hasDeadTeammate = true;
                }
            }
        }

        _respawnButton.gameObject.SetActive(hasTeammate);
        _respawnButton.interactable = hasFunds && hasDeadTeammate;
    }

    /// <summary>
    /// Deducts money and requests the local PlayerInstance to send the respawn RPC.
    /// UI elements themselves are often not spawned on the network, so we route
    /// network requests through the local player object.
    /// </summary>
    public void CallInBackup()
    {
        if (GlobalHostVariables.Instance == null ||
            PlayerInstance.Instance == null ||
            ReviveManager.Instance == null ||
            NetworkManager.Singleton == null)
        {
            return;
        }

        // Prevent duplicate click events while the authoritative revive request is in flight.
        if (_respawnButton != null)
            _respawnButton.interactable = false;

        // Find the first dead teammate
        ulong targetClientId = ulong.MaxValue;
        PlayerInstance corpse = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                var player = client.PlayerObject.GetComponent<PlayerInstance>();
                if (player != null &&
                    player != PlayerInstance.Instance &&
                    player.PlayerHealth != null &&
                    player.PlayerHealth.IsDead)
                {
                    targetClientId = client.ClientId;
                    corpse = player;
                    break;
                }
            }
        }

        if (targetClientId == ulong.MaxValue || corpse == null) return;

        // Attempt to deduct money from the shared pool
        GlobalHostVariables.Instance.SubtractMoneyFromClient(RespawnCost);

        // Route the revive request through ReviveManager — it handles despawning the
        // dead player object and spawning a fresh one at the lobby spawn point.
        ReviveManager.Instance.RevivePlayer(targetClientId, isNewDay: false);

        HangUp();
    }

    private void OnDisable()
    {
        if (_loopingAudio != null)
            _loopingAudio.Stop();
    }

    public void HangUp()
    {
        _telephone.HangUp(NetworkManager.Singleton.LocalClientId);
    }
}
