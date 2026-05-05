using Unity.Netcode;
using UnityEngine;

/// <summary>
/// An interactable generator that players must refill during the night phase.
/// Implements IBetweenShiftTask — networked visual state synced across all clients.
/// </summary>
public class GeneratorRefillTask : Interactable, IBetweenShiftTask
{
    [Header("Task")]
    [SerializeField] private AudioClip _refillSound;

    [Header("Indicators")]
    [Tooltip("Shown when the generator needs fuel (red light, etc.)")]
    [SerializeField] private GameObject _needsFuelIndicator;

    [Tooltip("Shown when the generator is fuelled (green light, etc.)")]
    [SerializeField] private GameObject _fuelledIndicator;

    private readonly NetworkVariable<bool> _isComplete = new(false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public string TaskName => "Refill Generator";
    public bool IsComplete => _isComplete.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isComplete.OnValueChanged += OnCompleteChanged;
        UpdateIndicators(_isComplete.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isComplete.OnValueChanged -= OnCompleteChanged;
    }

    private void OnCompleteChanged(bool oldValue, bool newValue)
    {
        UpdateIndicators(newValue);
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (_isComplete.Value) return;
        base.Interact(player);
        CompleteTaskServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void CompleteTaskServerRpc()
    {
        if (_isComplete.Value) return;
        _isComplete.Value = true;
        PlayRefillSoundClientRpc();
        BetweenShiftTaskManager.Instance.NotifyTaskComplete(this);
    }

    [ClientRpc]
    private void PlayRefillSoundClientRpc()
    {
        if (_refillSound != null)
            SFXController.Instance.Play(_refillSound);
    }

    /// <summary>Resets the task for the next night phase. Called on the server by BetweenShiftTaskManager.</summary>
    public void ResetTask()
    {
        if (!IsServer) return;
        _isComplete.Value = false;
    }

    private void UpdateIndicators(bool complete)
    {
        if (_needsFuelIndicator != null) _needsFuelIndicator.SetActive(!complete);
        if (_fuelledIndicator != null) _fuelledIndicator.SetActive(complete);
    }
}
