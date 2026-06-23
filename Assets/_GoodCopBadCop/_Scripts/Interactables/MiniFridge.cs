using Unity.Netcode;
using UnityEngine;

public class MiniFridge : Interactable
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fridgeOpenSound;
    [SerializeField] private AudioClip fridgeCloseSound;
    [SerializeField] private Animator animator;
    [SerializeField] private ElectricityController _electricityController;
    [SerializeField] private MiniFridgeDiegeticController _diegeticController;

    /// <summary>True when the fridge has an electricity source and that source is powered on.</summary>
    public bool IsPowered => _electricityController != null && _electricityController.IsPowerOn;

    private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isOpen.OnValueChanged += OnFridgeStateChanged;

        // Sync visual state on late join.
        animator.SetBool("Open", _isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isOpen.OnValueChanged -= OnFridgeStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_diegeticController != null && !_isOpen.Value)
        {
            // Open the door and enter the diegetic view together.
            RequestOpenServerRpc();
            _diegeticController.Open(player, this);
        }
        else
        {
            // No controller assigned, or door is already open — plain toggle.
            ToggleFridgeServerRpc();
        }
    }

    /// <summary>
    /// Closes the fridge door over the network.
    /// Called by <see cref="MiniFridgeDiegeticController"/> when the player exits the view.
    /// </summary>
    public void RequestClose()
    {
        if (_isOpen.Value)
            RequestCloseServerRpc();
    }

    // ─── ServerRpcs ──────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void ToggleFridgeServerRpc()
    {
        _isOpen.Value = !_isOpen.Value;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestOpenServerRpc()
    {
        _isOpen.Value = true;
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCloseServerRpc()
    {
        _isOpen.Value = false;
    }

    // ─── Callbacks ───────────────────────────────────────────────────────────

    private void OnFridgeStateChanged(bool oldValue, bool newValue)
    {
        animator.SetBool("Open", newValue);
        audioSource.PlayOneShot(newValue ? fridgeOpenSound : fridgeCloseSound);
    }
}
