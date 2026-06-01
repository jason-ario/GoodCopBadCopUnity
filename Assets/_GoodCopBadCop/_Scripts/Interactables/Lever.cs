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

    /// <summary>
    /// Raises the lever on the server and broadcasts visuals to all clients,
    /// opening the shutter. Call this from tutorial scripts that need to open
    /// the window automatically without player input.
    /// Must be called on the server.
    /// </summary>
    public void OpenServerSide()
    {
        if (!IsServer) return;
        _isUp.Value = true;
        BroadcastLeverStateClientRpc(true, ulong.MaxValue); // ulong.MaxValue = no exclusion
    }

    public void Reset()
    {
        if (!IsServer) return;

        // Only broadcast (and play the lever-off sound) when the lever was actually up.
        // If it is already down, the visual state is already correct on all clients and
        // firing the RPC would play a spurious sound — notably during the intro cutscene
        // when ResetEverything() runs before the lever has ever been touched.
        if (!_isUp.Value) return;

        _isUp.Value = false;
        BroadcastLeverStateClientRpc(false, ulong.MaxValue);
    }
}
