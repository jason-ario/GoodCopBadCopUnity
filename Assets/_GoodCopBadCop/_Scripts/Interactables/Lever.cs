using Unity.Netcode;
using UnityEngine;

public class Lever : Interactable
{
    [SerializeField] private Animator _animator;
    [SerializeField] private AudioSource leverAudio;
    [SerializeField] AudioClip leverOnSound;
    [SerializeField] AudioClip leverOffSound;
    [SerializeField] private ShutterController shutter;
    
    private static readonly int IsUpParam = Animator.StringToHash("IsUp");

    private NetworkVariable<bool> _isUp = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsUp => _isUp.Value;

    public override void OnNetworkSpawn()
    {
        _isUp.OnValueChanged += OnLeverStateChanged;

        // Sync visual state on spawn
        _animator.SetBool(IsUpParam, _isUp.Value);

        // Sync shutter state on spawn (silent — no audio on initial sync)
        if (_isUp.Value)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnLeverStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        // Apply visuals immediately on the interacting client — no RTT wait.
        bool predicted = !_isUp.Value;
        ApplyLeverVisuals(predicted);

        ToggleLeverServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleLeverServerRpc(ulong senderClientId)
    {
        _isUp.Value = !_isUp.Value;

        // Broadcast visuals to all clients except the one that already predicted.
        BroadcastLeverStateClientRpc(_isUp.Value, senderClientId);
    }

    /// <summary>
    /// Applies the lever visual to all clients except the one that predicted it locally.
    /// </summary>
    [ClientRpc]
    private void BroadcastLeverStateClientRpc(bool isUp, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;
        ApplyLeverVisuals(isUp);
    }

    private void OnLeverStateChanged(bool oldValue, bool newValue)
    {
        // Only used for late-joining clients that missed the BroadcastLeverStateClientRpc.
    }

    private void ApplyLeverVisuals(bool isUp)
    {
        _animator.SetBool(IsUpParam, isUp);
        leverAudio.PlayOneShot(isUp ? leverOnSound : leverOffSound);

        if (isUp)
            shutter.OpenShutter();
        else
            shutter.CloseShutter();
    }

    public void Reset()
    {
        _isUp.Value = false;
    }
}
