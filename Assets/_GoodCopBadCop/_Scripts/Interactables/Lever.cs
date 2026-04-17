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
    }

    public override void OnNetworkDespawn()
    {
        _isUp.OnValueChanged -= OnLeverStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleLeverServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleLeverServerRpc()
    {
        _isUp.Value = !_isUp.Value;
        if (_isUp.Value)
        {
            shutter.OpenShutter();
        }
        else
        {
            shutter.CloseShutter();
        }
    }

    private void OnLeverStateChanged(bool oldValue, bool newValue)
    {
        _animator.SetBool(IsUpParam, newValue);
        leverAudio.PlayOneShot(newValue ? leverOnSound : leverOffSound);
    }

    public void Reset()
    {
        _isUp.Value = false;
    }
}
