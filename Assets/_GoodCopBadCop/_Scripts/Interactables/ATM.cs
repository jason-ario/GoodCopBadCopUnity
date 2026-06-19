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

    [Tooltip("First boundary of the spawn range. Coupons appear at a random position between this and Spawn Point B.")]
    [SerializeField] private Transform _couponSpawnPointA;

    [Tooltip("Second boundary of the spawn range. Coupons appear at a random position between Spawn Point A and this.")]
    [SerializeField] private Transform _couponSpawnPointB;

    [Tooltip("Seconds between successive coupon spawns when multiple are dispensed at once.")]
    [SerializeField] private float _spawnInterval = 0.3f;

    [Header("Audio")]
    [Tooltip("Sound played on the ATM when it begins dispensing coupons.")]
    [SerializeField] private AudioClip _dispenseSfx;

    [Tooltip("Spatial audio max-distance for the dispense sound.")]
    [SerializeField] private float _dispenseSfxMaxDistance = 10f;

    [Header("Physics")]
    [Tooltip("Minimum angular velocity magnitude applied to each coupon on spawn.")]
    [SerializeField] private float _torqueMin = 1f;

    [Tooltip("Maximum angular velocity magnitude applied to each coupon on spawn.")]
    [SerializeField] private float _torqueMax = 5f;

    [Header("Effects")]
    [Tooltip("MachineShake component that runs while the ATM is dispensing. Disable the component in the Inspector; it will be toggled on/off automatically.")]
    [SerializeField] private MachineShake _machineShake;

    [Header("Debug")]
    [Tooltip("Number of coupons to dispense when pressing M in a development build or the editor.")]
    [SerializeField] private int _debugDispenseAmount = 5;

    private int _activeSpawnCount;

    /// <summary>
    /// True when there is no active NetworkManager session (editor / offline test)
    /// or when this peer is the server/host.
    /// </summary>
    private bool IsServer => NetworkManager.Singleton == null
        || !NetworkManager.Singleton.IsListening
        || NetworkManager.Singleton.IsServer;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;

        // Shake is driven by SpawnCouponsRoutine; ensure it starts off.
        if (_machineShake != null)
            _machineShake.enabled = false;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Input.GetKeyDown(KeyCode.M))
            SpawnCoupons(_debugDispenseAmount);
#endif
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

        Vector3 spawnPosition = (_couponSpawnPointA != null && _couponSpawnPointB != null)
            ? Vector3.Lerp(_couponSpawnPointA.position, _couponSpawnPointB.position, Random.value)
            : transform.position;

        Quaternion spawnRotation = _couponSpawnPointA.rotation;
        GameObject spawned = Instantiate(_couponPickupPrefab, spawnPosition, spawnRotation);

        bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        if (networked)
        {
            NetworkObject netObj = spawned.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[ATM] The coupon pickup prefab does not have a NetworkObject component.");
                Destroy(spawned);
                return;
            }
            netObj.Spawn(true);
        }

        ApplyRandomTorque(spawned);
    }

    private void ApplyRandomTorque(GameObject coupon)
    {
        Rigidbody rb = coupon.GetComponent<Rigidbody>();
        if (rb == null) return;

        Vector3 randomAxis = Random.onUnitSphere;
        float magnitude = Random.Range(_torqueMin, _torqueMax);
        rb.AddTorque(randomAxis * magnitude, ForceMode.Impulse);
    }
}
