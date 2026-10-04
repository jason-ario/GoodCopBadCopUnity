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
    private Coroutine _usePillBottleCoroutine;

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
        _usePillBottleCoroutine = StartCoroutine(UsePillBottle());
    }

    /// <summary>
    /// Taking a pill is a one-shot action that commits on click. Use-button release (which
    /// PlayerPickupController delivers as OnStopUse) must NOT cancel the in-progress dose —
    /// otherwise a normal click kills the coroutine a frame later and the pill never applies.
    /// Genuine interruptions (unequip, stow, force-clear) go through <see cref="CancelPillUse"/>.
    /// </summary>
    public override void OnStopUse()
    {
        if (_usePillBottleCoroutine != null) return;

        base.OnStopUse();
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        CancelPillUse();
        base.OnUnequip(player);
    }

    public override void ForceClearUseState()
    {
        base.ForceClearUseState();
        CancelPillUse();
    }

    /// <summary>
    /// Aborts an in-progress dose and restores the tightened near clip plane and drinking
    /// anim/animator state, so the effect never gets stuck engaged when the bottle is
    /// unequipped or stowed mid-drink (stow deactivates the GameObject, killing the coroutine).
    /// </summary>
    private void CancelPillUse()
    {
        if (_usePillBottleCoroutine == null) return;

        StopCoroutine(_usePillBottleCoroutine);
        _usePillBottleCoroutine = null;
        isUsing = false;

        if (playerPickupController != null)
        {
            playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", false);
            playerPickupController.PlayerAnimationController.EnableRightArmMask();
        }

        if (_animator != null)
            _animator.SetBool("TakePill", false);

        SetNearClipPlaneTightened(false);
    }

    IEnumerator UsePillBottle()
    {
        SFXController.Instance.Play(drinkSound);
        playerPickupController.PlayerAnimationController.EnableHoldObjectTwoArmsMask();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", true);
        _animator.SetBool("TakePill", true);
        SetNearClipPlaneTightened(true);
        yield return new WaitForSeconds(2.5f);
        PlayerInstance.Instance.PlayerRadiation.TakeRadiationPill();
        playerPickupController.PlayerAnimationController.SetAnimBool("TakingPill", false);
        _animator.SetBool("TakePill", false);
        SetNearClipPlaneTightened(false);

        ConsumePillServerRpc();

        playerPickupController.PlayerAnimationController.EnableRightArmMask();
        isUsing = false;
        _usePillBottleCoroutine = null;
    }

    /// <summary>Tightens (or restores) the player's render camera near clip plane while drinking pills, mirroring the cigarette's effect.</summary>
    private void SetNearClipPlaneTightened(bool isDrinking)
    {
        if (playerPickupController == null) return;

        PlayerCameraController cameraController = playerPickupController.GetComponent<PlayerCameraController>();
        if (cameraController == null) return;

        cameraController.SetNearClipPlaneTightened(isDrinking);
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
