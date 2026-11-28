using System;
using System.Collections;
using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Glass vodka bottle pickup.
/// Drinking: LMB takes a sip, reusing the radiation-pill drink animation. Each sip counts toward
/// the drinker's <see cref="PlayerDrunkState"/>, adds a little radiation and heals them if they are
/// hurt. After <see cref="MaxSips"/> sips the bottle is empty and can no longer be drunk.
/// Breaking: a hard enough impact while thrown, or a bullet/melee hit, shatters it on every client
/// through its <see cref="ShatterableProp"/> (glass shards, dust, liquid splash unless empty,
/// shatter sound). The server then despawns the networked item so it is gone for everyone,
/// including late joiners and saves.
/// </summary>
[RequireComponent(typeof(ShatterableProp))]
public class VodkaBottle : PickableObject, IAmmoProvider
{
    private const int MaxSips = 6;
    private const string DrinkAnimBool = "TakingPill";

    [Header("Drinking")]
    [SerializeField] private AudioClip _drinkSound;
    [SerializeField] private float _drinkSoundVolume = 1f;

    [Tooltip("Length of one sip; matches the radiation-pill drink animation. The sip counts when it finishes.")]
    [SerializeField] private float _sipDuration = 2.5f;

    [Tooltip("Radiation added to the drinker per sip.")]
    [SerializeField, Min(0f)] private float _radiationPerSip = 5f;

    [Tooltip("Health restored per sip. Only applied when the drinker is below max health.")]
    [SerializeField, Min(0f)] private float _healPerSip = 10f;

    [Header("Break On Throw")]
    [Tooltip("Minimum impact speed (m/s) for a thrown bottle to smash. Gentle tosses just bounce.")]
    [SerializeField] private float _minSmashSpeed = 3f;

    [Tooltip("Seconds between shattering and the server despawning the item.")]
    [SerializeField] private float _despawnDelay = 0.25f;

    private readonly NetworkVariable<int> _sipsRemaining = new(MaxSips,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ShatterableProp _shatter;
    private bool _breakingServer;
    private Coroutine _sipCoroutine;

    public bool IsEmpty => _sipsRemaining.Value <= 0;

    // IAmmoProvider: shows sips left in the HUD ammo counter / inventory slot.
    public float CurrentAmmo => _sipsRemaining.Value;
    public float MaxAmmo => MaxSips;
    public event Action OnAmmoChanged;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _shatter = GetComponent<ShatterableProp>();
        if (_shatter != null) _shatter.Shattered += OnShattered;
        _sipsRemaining.OnValueChanged += OnSipsRemainingChanged;
        ApplyLiquidState();
    }

    public override void OnNetworkDespawn()
    {
        if (_shatter != null) _shatter.Shattered -= OnShattered;
        _sipsRemaining.OnValueChanged -= OnSipsRemainingChanged;
        base.OnNetworkDespawn();
    }

    private void OnSipsRemainingChanged(int previous, int current)
    {
        ApplyLiquidState();
        OnAmmoChanged?.Invoke();
    }

    /// <summary>An empty bottle breaks without a liquid splash.</summary>
    private void ApplyLiquidState()
    {
        if (_shatter != null) _shatter.LiquidEnabled = !IsEmpty;
    }

    protected override void CaptureMutableSaveData(PickableObjectSaveData data)
    {
        data.HasResourceAmount = true;
        data.ResourceAmount = _sipsRemaining.Value;
    }

    protected override void RestoreMutableSaveData(PickableObjectSaveData data)
    {
        if (data.HasResourceAmount)
            _sipsRemaining.Value = Mathf.Clamp(Mathf.RoundToInt(data.ResourceAmount), 0, MaxSips);
    }

    // ── Drinking ────────────────────────────────────────────────────────────

    /// <summary>LMB takes a sip; no prompt once the bottle is empty.</summary>
    public override string GetHeldUseVerb() => IsEmpty ? null : Loc.N("Drink");

    public override void OnStartUse()
    {
        if (isUsing || IsEmpty || playerPickupController == null) return;

        base.OnStartUse();
        _sipCoroutine = StartCoroutine(TakeSip());
    }

    /// <summary>
    /// A sip commits on click, like the pill bottle: releasing LMB (delivered as OnStopUse) must
    /// not cancel it. Real interruptions (unequip, stow, force-clear) go through <see cref="CancelSip"/>.
    /// </summary>
    public override void OnStopUse()
    {
        if (_sipCoroutine != null) return;

        base.OnStopUse();
    }

    public override void OnUnequip(PlayerPickupController player)
    {
        CancelSip();
        base.OnUnequip(player);
    }

    public override void ForceClearUseState()
    {
        base.ForceClearUseState();
        CancelSip();
    }

    private IEnumerator TakeSip()
    {
        if (_drinkSound != null && SFXController.Instance != null)
            SFXController.Instance.Play(_drinkSound, _drinkSoundVolume);

        SetDrinkPose(true);
        yield return new WaitForSeconds(_sipDuration);
        SetDrinkPose(false);

        DrinkServerRpc();

        isUsing = false;
        _sipCoroutine = null;
    }

    /// <summary>Aborts an in-progress sip and restores the arm mask, anim bool and near clip plane.</summary>
    private void CancelSip()
    {
        if (_sipCoroutine == null) return;

        StopCoroutine(_sipCoroutine);
        _sipCoroutine = null;
        isUsing = false;
        SetDrinkPose(false);
    }

    private void SetDrinkPose(bool drinking)
    {
        if (playerPickupController == null) return;

        PlayerAnimationController anim = playerPickupController.PlayerAnimationController;
        if (anim != null)
        {
            if (drinking) anim.EnableHoldObjectTwoArmsMask();
            anim.SetAnimBool(DrinkAnimBool, drinking);
            if (!drinking) anim.EnableRightArmMask();
        }

        // Keeps the bottle from clipping through the camera when it reaches the mouth.
        PlayerCameraController cameraController = playerPickupController.GetComponent<PlayerCameraController>();
        if (cameraController != null) cameraController.SetNearClipPlaneTightened(drinking);
    }

    [Rpc(SendTo.Server)]
    private void DrinkServerRpc(RpcParams rpcParams = default)
    {
        if (IsEmpty) return;

        _sipsRemaining.Value--;

        ulong drinker = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(drinker, out NetworkClient client) || client.PlayerObject == null)
            return;

        GameObject player = client.PlayerObject.gameObject;

        PlayerDrunkState drunkState = player.GetComponent<PlayerDrunkState>();
        if (drunkState != null) drunkState.RegisterDrinkServer();

        PlayerRadiation radiation = player.GetComponent<PlayerRadiation>();
        if (radiation != null && _radiationPerSip > 0f) radiation.AddRadiation(_radiationPerSip);

        // Heal only when hurt, so a healthy drinker doesn't get a pointless heal flash.
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && _healPerSip > 0f && !health.IsDead && health.Health < health.MaxHealth)
            health.Heal(_healPerSip, EffectKeys.FoodHeal);
    }

    // ── Breaking ────────────────────────────────────────────────────────────

    protected override void OnPlayerThrowImpactServer(Collision collision, ulong throwerClientId)
    {
        if (_breakingServer || _shatter == null || _shatter.IsShattered) return;
        if (collision.relativeVelocity.magnitude < _minSmashSpeed) return;
        if (IsThrowerBody(collision.collider, throwerClientId)) return;

        ContactPoint contact = collision.GetContact(0);
        Vector3 incoming = -collision.relativeVelocity;
        Vector3 splashDir = (contact.normal + Vector3.Reflect(incoming.normalized, contact.normal)).normalized;
        if (splashDir.sqrMagnitude < 0.0001f) splashDir = contact.normal;

        SmashClientRpc(contact.point, splashDir);

        // Covers a dedicated server, where the ClientRpc does not run locally.
        _shatter.Hit(contact.point, splashDir);
        BeginBreakServer();
    }

    [ClientRpc]
    private void SmashClientRpc(Vector3 point, Vector3 direction)
    {
        if (_shatter != null) _shatter.Hit(point, direction);
    }

    /// <summary>Shot or smashed on this machine. The server removes the networked item.</summary>
    private void OnShattered(Vector3 point, Vector3 direction)
    {
        if (IsServer) BeginBreakServer();
    }

    private void BeginBreakServer()
    {
        if (_breakingServer || !IsSpawned) return;
        _breakingServer = true;

        // The shatter disabled every collider, so freeze the body instead of letting it fall away.
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }

        StartCoroutine(DespawnAfterDelay());
    }

    private IEnumerator DespawnAfterDelay()
    {
        yield return new WaitForSeconds(_despawnDelay);
        if (IsSpawned) NetworkHelper.Despawn(NetworkObject);
    }

    private bool IsThrowerBody(Collider other, ulong throwerClientId)
    {
        NetworkManager nm = NetworkManager;
        if (nm == null || other == null) return false;
        if (!nm.ConnectedClients.TryGetValue(throwerClientId, out NetworkClient client)) return false;
        return client.PlayerObject != null && other.transform.IsChildOf(client.PlayerObject.transform);
    }
}
