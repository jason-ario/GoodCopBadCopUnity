using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the "Call in Backup" logic in the HQ Order Screen.
/// Handles money deduction and requests the local player to send a respawn RPC to the server.
/// The option is always shown; when it can't be used, it is disabled and <see cref="_statusText"/>
/// explains why, so the screen is never empty.
/// </summary>
public class HQOrderScreen : MonoBehaviour
{
    [SerializeField] private Telephone _telephone;
    [SerializeField] private AudioSource _loopingAudio;
    [SerializeField] private Button _respawnButton;
    [SerializeField] private TextMeshProUGUI _respawnButtonText;

    [Header("Info")]
    [Tooltip("Line under the order options explaining availability of the backup order.")]
    [SerializeField] private TextMeshProUGUI _statusText;
    [Tooltip("Shows the shared money pool.")]
    [SerializeField] private TextMeshProUGUI _fundsText;

    private const int RespawnCost = 10;
    private const string RespawnTextFormat = "Call in Backup  <sprite=0> {0}";
    private const string FundsTextFormat = "Station Funds  <sprite=0> {0}";

    private const string StatusSolo = "No partner on record. Backup is unavailable for solo shifts.";
    private const string StatusTeammateAlive = "Your partner is alive and on duty. No backup required.";
    private const string StatusNoFunds = "Insufficient funds. Backup costs <sprite=0> {0}.";
    private const string StatusReady = "Your partner is down. HQ can send a replacement for <sprite=0> {0}.";

    private int _lastFunds = int.MinValue;
    private string _lastStatus;

    private void OnEnable()
    {
        if (_loopingAudio != null)
            _loopingAudio.Play();

        if (_respawnButtonText != null)
            _respawnButtonText.text = string.Format(RespawnTextFormat, RespawnCost);

        _lastFunds = int.MinValue;
        _lastStatus = null;
        UpdateRespawnButton();
    }

    private void Update()
    {
        UpdateRespawnButton();
    }

    private void UpdateRespawnButton()
    {
        if (NetworkManager.Singleton == null) return;

        int funds = GlobalHostVariables.Instance != null ? GlobalHostVariables.Instance.money.Value : 0;
        bool hasFunds = funds >= RespawnCost;
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

        if (_respawnButton != null)
            _respawnButton.interactable = hasFunds && hasDeadTeammate;

        if (_fundsText != null && funds != _lastFunds)
        {
            _lastFunds = funds;
            _fundsText.text = string.Format(FundsTextFormat, funds);
        }

        if (_statusText != null)
        {
            string status = !hasTeammate ? StatusSolo
                : !hasDeadTeammate ? StatusTeammateAlive
                : !hasFunds ? string.Format(StatusNoFunds, RespawnCost)
                : string.Format(StatusReady, RespawnCost);

            if (status != _lastStatus)
            {
                _lastStatus = status;
                _statusText.text = status;
            }
        }
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
        // dead player object and spawning a fresh one (booth on Day 1 before shift end,
        // otherwise the lobby spawn point).
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
