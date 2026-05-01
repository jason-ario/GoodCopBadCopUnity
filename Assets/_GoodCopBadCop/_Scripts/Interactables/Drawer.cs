using Unity.Netcode;
using UnityEngine;

public class Drawer : Interactable
{
    [SerializeField] private Animator animator;
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip drawerOpenSound;
    [SerializeField] AudioClip drawerCloseSound;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isOpen.OnValueChanged += OnDrawerStateChanged;

        // Sync visual on late join
        animator.SetBool("Open", isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged -= OnDrawerStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleDrawerServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDrawerServerRpc()
    {
        isOpen.Value = !isOpen.Value;
    }

    private void OnDrawerStateChanged(bool oldValue, bool newValue)
    {
        animator.SetBool("Open", newValue);
        audioSource.PlayOneShot(newValue ? drawerOpenSound : drawerCloseSound);
    }
}
