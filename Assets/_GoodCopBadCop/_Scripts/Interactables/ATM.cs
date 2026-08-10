using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the physical coupon dispense flow.
/// When cash is earned, plays a dispense sound and spawns <see cref="CouponPickup"/>
/// NetworkObjects one after another at <see cref="_couponSpawnPoint"/>.
/// Spawning is server-authoritative; the singleton is readable from all contexts.
///
/// Electricity responsiveness:
///   Add an <see cref="ElectricObject"/> to this GameObject and register it with the booth's
///   <see cref="ElectricityController"/>. Wire its events:
///     OnElectricityTurnOn  → ATM.OnElectricityOn
///     OnElectricityTurnOff → ATM.OnElectricityOff
///   While unpowered, <see cref="SpawnCoupons"/> is a no-op — the ATM simply won't dispense.
/// </summary>
public class ATM : NetworkBehaviour
{
    public static ATM Instance;

    [Header("Electricity")]
    [Tooltip("Whether the ATM currently has power. Defaults to true so behavior is unchanged " +
             "if no ElectricObject is wired up. Driven by OnElectricityOn/OnElectricityOff.")]
    [SerializeField] private bool _isPowered = true;

    /// <summary>True while the ATM has electricity and can dispense coupons.</summary>
    public bool IsPowered => _isPowered;

    [Header("Spawning")]
    [Tooltip("Prefab that has a CouponPickup component and a NetworkObject. Its CouponValue is the larger denomination (e.g. 5) used to make up most of a payout.")]
    [SerializeField] private GameObject _couponPickupPrefab;

    [Tooltip("Prefab that has a CouponPickup component and a NetworkObject. Its CouponValue is the smaller denomination (e.g. 1) used to make up any remainder that the larger denomination can't cover exactly.")]
    [SerializeField] private GameObject _couponPickupPrefabOnes;

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

    [Tooltip("Sound played at the spawn position each time a single coupon is ejected.")]
    [SerializeField] private AudioClip _couponSpawnSfx;

    [Tooltip("Spatial audio max-distance for the per-coupon spawn sound.")]
    [SerializeField] private float _couponSpawnSfxMaxDistance = 5f;

    [Header("Physics")]
    [Tooltip("Minimum angular velocity magnitude applied to each coupon on spawn.")]
    [SerializeField] private float _torqueMin = 1f;

    [Tooltip("Maximum angular velocity magnitude applied to each coupon on spawn.")]
    [SerializeField] private float _torqueMax = 5f;

    [Header("Effects")]
    [Tooltip("MachineShake component that runs while the ATM is dispensing. Disable the component in the Inspector; it will be toggled on/off automatically.")]
    [SerializeField] private MachineShake _machineShake;

    [Tooltip("Screen controller that flashes the payment amount on the ATM display.")]
    [SerializeField] private ATMScreenController _screenController;

    [Tooltip("Seconds to wait after coupon spawning begins before showing the payment amount on the ATM screen.")]
    [SerializeField] private float _paymentTextDelay = 2f;

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

    /// <summary>
    /// True when there is an active NetworkManager session that is listening.
    /// </summary>
    private bool IsNetworked => NetworkManager.Singleton != null
        && NetworkManager.Singleton.IsListening;

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
    /// Plays the ATM dispense sound and spawns coupon pickup objects one after another
    /// at the spawn point. <paramref name="amount"/> is first scaled by the current
    /// Checkpoint Integrity Score (see <see cref="CheckpointIntegrityService"/>) — a dirty,
    /// trashed, or fence-damaged booth pays out less on every ATM transaction. The resulting
    /// amount is broken down into a mix of coupon denominations (largest first, e.g. 5s then
    /// 1s) so the physically spawned coupons sum exactly to the payout. SERVER ONLY.
    /// </summary>
    public void SpawnCoupons(int amount)
    {
        if (!IsServer) return;
        if (amount <= 0) return;

        if (!_isPowered)
        {
            Debug.Log("[ATM] No electricity — coupons not dispensed.");
            return;
        }

        int adjustedAmount = CheckpointIntegrityService.Instance != null
            ? CheckpointIntegrityService.Instance.ApplyMultiplier(amount)
            : amount;

        List<GameObject> spawnQueue = BuildSpawnQueue(adjustedAmount);
        if (spawnQueue.Count == 0)
        {
            Debug.LogError("[ATM] No coupon prefabs assigned in the Inspector — cannot dispense.");
            return;
        }

        PlayDispenseSound();

        StartCoroutine(SpawnCouponsRoutine(spawnQueue));
        StartCoroutine(ShowPaymentDelayedRoutine(adjustedAmount));
    }

    // ── Electricity callbacks — wire via Inspector through ElectricObject events ────

    /// <summary>
    /// Wire to the <see cref="ElectricObject.OnElectricityTurnOn"/> UnityEvent on this
    /// GameObject's ElectricObject component in the Inspector.
    /// </summary>
    public void OnElectricityOn()
    {
        _isPowered = true;
    }

    /// <summary>
    /// Wire to the <see cref="ElectricObject.OnElectricityTurnOff"/> UnityEvent on this
    /// GameObject's ElectricObject component in the Inspector.
    /// </summary>
    public void OnElectricityOff()
    {
        _isPowered = false;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Breaks <paramref name="amount"/> down into a greedy mix of the configured coupon
    /// denominations (largest CouponValue first), returning the prefab to spawn for each
    /// physical coupon, in spawn order. If the denominations can't reach the amount exactly
    /// (e.g. only a 5-value prefab is assigned and the amount isn't a multiple of 5), any
    /// remainder is made up using the smallest available denomination.
    /// </summary>
    private List<GameObject> BuildSpawnQueue(int amount)
    {
        var result = new List<GameObject>();

        var denominations = new List<(GameObject prefab, int value)>();
        foreach (GameObject prefab in new[] { _couponPickupPrefab, _couponPickupPrefabOnes })
        {
            if (prefab == null) continue;
            CouponPickup pickup = prefab.GetComponent<CouponPickup>();
            int value = pickup != null ? pickup.CouponValue : 1;
            if (value > 0) denominations.Add((prefab, value));
        }

        if (denominations.Count == 0) return result;

        denominations.Sort((a, b) => b.value.CompareTo(a.value));

        int remaining = amount;
        foreach ((GameObject prefab, int value) in denominations)
        {
            int count = remaining / value;
            for (int i = 0; i < count; i++) result.Add(prefab);
            remaining -= count * value;
        }

        // Ensure at least one coupon is spawned, and cover any leftover remainder,
        // using the smallest configured denomination.
        if (remaining > 0 || result.Count == 0)
        {
            (GameObject prefab, int value) smallest = denominations[denominations.Count - 1];
            result.Add(smallest.prefab);
        }

        return result;
    }

    private void PlayDispenseSound()
    {
        if (_dispenseSfx == null) return;

        if (IsNetworked)
            PlayDispenseSoundClientRpc();
        else
            SFXController.Instance?.PlayAtPosition(
                _dispenseSfx,
                transform.position,
                maxDistance: _dispenseSfxMaxDistance
            );
    }

    /// <summary>
    /// Plays the per-coupon ejection sound at the given world position.
    /// </summary>
    private void PlayCouponSpawnSound(Vector3 position)
    {
        if (_couponSpawnSfx == null) return;

        if (IsNetworked)
            PlayCouponSpawnSoundClientRpc(position);
        else
            SFXController.Instance?.PlayAtPosition(
                _couponSpawnSfx,
                position,
                maxDistance: _couponSpawnSfxMaxDistance
            );
    }

    /// <summary>
    /// Waits <see cref="_paymentTextDelay"/> seconds after coupon spawning begins,
    /// then shows the payment amount on the ATM screen (locally or via RPC to all clients).
    /// </summary>
    private IEnumerator ShowPaymentDelayedRoutine(int amount)
    {
        if (_paymentTextDelay > 0f)
            yield return new WaitForSeconds(_paymentTextDelay);

        if (IsNetworked)
            ShowPaymentClientRpc(amount);
        else
            _screenController?.ShowPayment(amount);
    }

    private IEnumerator SpawnCouponsRoutine(List<GameObject> spawnQueue)
    {
        _activeSpawnCount++;
        if (_machineShake != null) _machineShake.enabled = true;

        for (int i = 0; i < spawnQueue.Count; i++)
        {
            SpawnOneCoupon(spawnQueue[i]);
            yield return new WaitForSeconds(_spawnInterval);
        }

        _activeSpawnCount--;
        if (_activeSpawnCount <= 0 && _machineShake != null)
            _machineShake.enabled = false;
    }

    private void SpawnOneCoupon(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("[ATM] Attempted to spawn a null coupon prefab.");
            return;
        }

        Vector3 spawnPosition = (_couponSpawnPointA != null && _couponSpawnPointB != null)
            ? Vector3.Lerp(_couponSpawnPointA.position, _couponSpawnPointB.position, Random.value)
            : transform.position;

        Quaternion spawnRotation = _couponSpawnPointA.rotation;
        GameObject spawned = Instantiate(prefab, spawnPosition, spawnRotation);

        PlayCouponSpawnSound(spawnPosition);

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

    // ── Network RPCs ─────────────────────────────────────────────────────────

    /// <summary>
    /// Tells every client (including host) to show the payment amount on the ATM screen.
    /// Called by the server after a coupon dispense begins.
    /// </summary>
    [ClientRpc]
    private void ShowPaymentClientRpc(int amount)
    {
        _screenController?.ShowPayment(amount);
    }

    /// <summary>
    /// Tells every client (including host) to play the ATM dispense sound. Called by the
    /// server so the sound is heard by all players, not just the one running the server.
    /// </summary>
    [ClientRpc]
    private void PlayDispenseSoundClientRpc()
    {
        SFXController.Instance?.PlayAtPosition(
            _dispenseSfx,
            transform.position,
            maxDistance: _dispenseSfxMaxDistance
        );
    }

    /// <summary>
    /// Tells every client (including host) to play the per-coupon ejection sound at
    /// <paramref name="position"/>. Called by the server for each coupon spawned so the
    /// sound is heard by all players, not just the one running the server.
    /// </summary>
    [ClientRpc]
    private void PlayCouponSpawnSoundClientRpc(Vector3 position)
    {
        SFXController.Instance?.PlayAtPosition(
            _couponSpawnSfx,
            position,
            maxDistance: _couponSpawnSfxMaxDistance
        );
    }
}
