using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class PillBottle : PickableObject, IAmmoProvider
{
    private const int MaxUses = 3;

    [SerializeField] Animator _animator;
    private readonly NetworkVariable<int> _usesRemaining = new(MaxUses, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private AudioClip drinkSound;

    // ── IAmmoProvider ─────────────────────────────────────────────────────────

    public float CurrentAmmo => _usesRemaining.Value;
    public float MaxAmmo => MaxUses;
    public event Action OnAmmoChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _usesRemaining.OnValueChanged += OnUsesRemainingChanged;
    }

    public override void OnNetworkDespawn()
    {
        _usesRemaining.OnValueChanged -= OnUsesRemainingChanged;
        base.OnNetworkDespawn();
    }

    private void OnUsesRemainingChanged(int _, int __) => OnAmmoChanged?.Invoke();

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = _usesRemaining.Value;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _usesRemaining.Value = Mathf.Clamp(Mathf.RoundToInt(data.ResourceAmount), 0, MaxUses);
    }

    /// <summary>
    /// Initiates a pill use if the bottle still has doses and is not already in use.
    /// Destroys the bottle after the last dose is consumed.
    /// </summary>
    public override void OnStartUse()
    {
        if (isUsing || _usesRemaining.Value <= 0) return;

        base.OnStartUse();
        StartCoroutine(UsePillBottle());
    }

    IEnumerator UsePillBottle()
    {
        SFXController.Instance.Play(drinkSound);
        playerPickupController.PlayerAnimationController.EnableHoldObjectTwoArmsMask();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", true);
        _animator.SetBool("TakePill", true);
        yield return new WaitForSeconds(2.5f);
        PlayerInstance.Instance.PlayerRadiation.TakeRadiationPill();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", false);
        _animator.SetBool("TakePill", false);

        ConsumePillServerRpc();

        playerPickupController.PlayerAnimationController.EnableRightArmMask();
        isUsing = false;
    }

    [Rpc(SendTo.Server)]
    private void ConsumePillServerRpc()
    {
        if (_usesRemaining.Value <= 0) return;

        _usesRemaining.Value--;
        if (_usesRemaining.Value <= 0 && NetworkObject != null && NetworkObject.IsSpawned)
            NetworkHelper.Despawn(NetworkObject);
    }
}
