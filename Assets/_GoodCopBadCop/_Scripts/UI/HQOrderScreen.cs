using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the "Call in Backup" logic in the HQ Order Screen.
/// Availability is read from <see cref="ReviveManager"/>'s server-replicated roster and the
/// revive is resolved server-side. Backup is free: the only requirement is a dead teammate.
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

    // Backup is intentionally free. ReviveManager skips the charge when the cost is 0.
    private const int RespawnCost = 0;
    private const string RespawnText = "Call in Backup";
    private const string FundsTextFormat = "Station Funds  <sprite=0> {0}";

    private const string StatusSolo = "No partner on record. Backup is unavailable for solo shifts.";
    private const string StatusTeammateAlive = "Your partner is alive and on duty. No backup required.";
    private const string StatusReady = "Your partner is down. HQ can send a replacement.";

    private int _lastFunds = int.MinValue;
    private string _lastStatus;

    private void OnEnable()
    {
        if (_loopingAudio != null)
            _loopingAudio.Play();

        if (_respawnButtonText != null)
            _respawnButtonText.text = RespawnText;

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

        // Read the server-replicated roster: ConnectedClientsList / PlayerObject are not reliable on
        // non-host clients, which previously left player 2 unable to see (and revive) a dead host.
        ulong localClientId = NetworkManager.Singleton.LocalClientId;
        ReviveManager revive = ReviveManager.Instance;
        bool hasTeammate = revive != null && revive.HasTeammate(localClientId);
        bool hasDeadTeammate = revive != null && revive.HasDeadTeammate(localClientId);

        if (_respawnButton != null)
            _respawnButton.interactable = hasDeadTeammate;

        if (_fundsText != null && funds != _lastFunds)
        {
            _lastFunds = funds;
            _fundsText.text = string.Format(FundsTextFormat, funds);
        }

        if (_statusText != null)
        {
            string status = !hasTeammate ? StatusSolo
                : !hasDeadTeammate ? StatusTeammateAlive
                : StatusReady;

            if (status != _lastStatus)
            {
                _lastStatus = status;
                _statusText.text = status;
            }
        }
    }

    /// <summary>
    /// Asks the server to revive a dead teammate. The server picks the target from its
    /// authoritative client list and replaces the dead player object. Free of charge.
    /// </summary>
    public void CallInBackup()
    {
        ReviveManager revive = ReviveManager.Instance;
        if (revive == null || NetworkManager.Singleton == null)
            return;

        if (!revive.HasDeadTeammate(NetworkManager.Singleton.LocalClientId))
            return;

        // Prevent duplicate click events while the authoritative revive request is in flight.
        if (_respawnButton != null)
            _respawnButton.interactable = false;

        revive.RequestReviveDeadTeammate(RespawnCost);

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
