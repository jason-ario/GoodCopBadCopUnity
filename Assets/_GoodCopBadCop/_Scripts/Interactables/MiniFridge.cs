using Unity.Netcode;
using UnityEngine;

public class MiniFridge : Interactable
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fridgeOpenSound;
    [SerializeField] private AudioClip fridgeCloseSound;
    [SerializeField] private Animator animator;
    [SerializeField] private ElectricityController _electricityController;

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

        // Sync visual state on late join
        animator.SetBool("Open", _isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isOpen.OnValueChanged -= OnFridgeStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleFridgeServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleFridgeServerRpc()
    {
        _isOpen.Value = !_isOpen.Value;
    }

    private void OnFridgeStateChanged(bool oldValue, bool newValue)
    {
        animator.SetBool("Open", newValue);
        audioSource.PlayOneShot(newValue ? fridgeOpenSound : fridgeCloseSound);
    }
}
