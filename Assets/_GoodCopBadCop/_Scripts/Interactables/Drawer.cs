using System;
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

    private bool _locked = false;

    /// <summary>
    /// Prevents interaction when true. Local-only — used as a tutorial gate on Day 1.
    /// </summary>
    public void SetLocked(bool locked) => _locked = locked;

    /// <summary>
    /// Fired locally whenever this drawer transitions to open.
    /// Subscribe to hide tutorial arrows or trigger other one-shot reactions.
    /// </summary>
    public event Action OnOpened;

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
        if (_locked) return;

        base.Interact(player);

        // Apply visuals immediately on the interacting client — no RTT wait.
        bool predicted = !isOpen.Value;
        ApplyDrawerVisuals(predicted);

        ToggleDrawerServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDrawerServerRpc(ulong senderClientId)
    {
        isOpen.Value = !isOpen.Value;

        // Broadcast visuals to all clients except the one that already predicted.
        BroadcastDrawerStateClientRpc(isOpen.Value, senderClientId);
    }

    /// <summary>
    /// Applies the drawer visual to all clients except the one that predicted it locally.
    /// </summary>
    [ClientRpc]
    private void BroadcastDrawerStateClientRpc(bool open, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;
        ApplyDrawerVisuals(open);
    }

    private void OnDrawerStateChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastDrawerStateClientRpc.
    }

    private void ApplyDrawerVisuals(bool open)
    {
        animator.SetBool("Open", open);
        audioSource.PlayOneShot(open ? drawerOpenSound : drawerCloseSound);

        if (open)
            OnOpened?.Invoke();
    }
}
