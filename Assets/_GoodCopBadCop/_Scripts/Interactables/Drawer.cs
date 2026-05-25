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

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Prevents interaction when true. Networked — propagates to all clients and
    /// is applied to late-joiners via the NetworkVariable sync on spawn.
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (IsServer)
            _isLocked.Value = locked;
        else
            SetLockedServerRpc(locked);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLockedServerRpc(bool locked) => _isLocked.Value = locked;

    /// <summary>
    /// Fired locally whenever this drawer transitions to open.
    /// Subscribe to hide tutorial arrows or trigger other one-shot reactions.
    /// </summary>
    public event Action OnOpened;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isOpen.OnValueChanged    += OnDrawerStateChanged;
        _isLocked.OnValueChanged += OnLockedChanged;

        // Sync visual on late join
        animator.SetBool("Open", isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        isOpen.OnValueChanged    -= OnDrawerStateChanged;
        _isLocked.OnValueChanged -= OnLockedChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value) return;

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

    private void OnLockedChanged(bool oldValue, bool newValue)
    {
        // Interactability is checked on each Interact() call via _isLocked.Value —
        // no visual change needed; this callback exists for late-joiner correctness.
    }

    private void ApplyDrawerVisuals(bool open)
    {
        animator.SetBool("Open", open);
        audioSource.PlayOneShot(open ? drawerOpenSound : drawerCloseSound);

        if (open)
            OnOpened?.Invoke();
    }
}
