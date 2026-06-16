using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the physical coupon dispense flow.
/// When cash is earned, plays a dispense sound and spawns <see cref="CouponPickup"/>
/// NetworkObjects one after another at <see cref="_couponSpawnPoint"/>.
/// Spawning is server-authoritative; the singleton is readable from all contexts.
/// </summary>
public class ATM : MonoBehaviour
{
    public static ATM Instance;

    [Header("Spawning")]
    [Tooltip("Prefab that has a CouponPickup component and a NetworkObject. Spawned for each coupon unit.")]
    [SerializeField] private GameObject _couponPickupPrefab;

    [Tooltip("World-space point where coupons appear. Assign the CouponSpawnPoint child Transform.")]
    [SerializeField] private Transform _couponSpawnPoint;

    [Tooltip("Seconds between successive coupon spawns when multiple are dispensed at once.")]
    [SerializeField] private float _spawnInterval = 0.3f;

    [Header("Audio")]
    [Tooltip("Sound played on the ATM when it begins dispensing coupons.")]
    [SerializeField] private AudioClip _dispenseSfx;

    [Tooltip("Spatial audio max-distance for the dispense sound.")]
    [SerializeField] private float _dispenseSfxMaxDistance = 10f;

    [Header("Effects")]
    [Tooltip("MachineShake component that runs while the ATM is dispensing. Disable the component in the Inspector; it will be toggled on/off automatically.")]
    [SerializeField] private MachineShake _machineShake;

    private int _activeSpawnCount;
    private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the ATM dispense sound and spawns <paramref name="amount"/> coupon pickup
    /// objects one after another at the spawn point. SERVER ONLY.
    /// </summary>
    public void SpawnCoupons(int amount)
    {
        if (!IsServer) return;
        if (amount <= 0) return;

        PlayDispenseSound();
        StartCoroutine(SpawnCouponsRoutine(amount));
    }

    // ── Private ──────────────────────────────────────────────────────────────

    private void PlayDispenseSound()
    {
        if (_dispenseSfx == null || SFXController.Instance == null) return;

        SFXController.Instance.PlayAtPosition(
            _dispenseSfx,
            transform.position,
            maxDistance: _dispenseSfxMaxDistance
        );
    }

    private IEnumerator SpawnCouponsRoutine(int amount)
    {
        _activeSpawnCount++;
        if (_machineShake != null) _machineShake.enabled = true;

        for (int i = 0; i < amount; i++)
        {
            SpawnOneCoupon();
            yield return new WaitForSeconds(_spawnInterval);
        }

        _activeSpawnCount--;
        if (_activeSpawnCount <= 0 && _machineShake != null)
            _machineShake.enabled = false;
    }

    private void SpawnOneCoupon()
    {
        if (_couponPickupPrefab == null)
        {
            Debug.LogError("[ATM] _couponPickupPrefab is not assigned in the Inspector.");
            return;
        }

        Vector3 spawnPosition = _couponSpawnPoint != null
            ? _couponSpawnPoint.position
            : transform.position;

        GameObject spawned = Instantiate(_couponPickupPrefab, spawnPosition, Quaternion.identity);
        NetworkObject netObj = spawned.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[ATM] The coupon pickup prefab does not have a NetworkObject component.");
            Destroy(spawned);
            return;
        }

        netObj.Spawn(true);
    }
}
