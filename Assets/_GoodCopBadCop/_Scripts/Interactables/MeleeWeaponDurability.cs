using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks networked durability for a melee weapon.
/// Each hit (enemy or environment) drains durability by the configured amount.
/// When durability reaches zero the weapon is despawned and <see cref="OnDepleted"/> fires.
/// Attach this alongside <see cref="PickableObject"/> on the weapon prefab and assign a
/// <see cref="MeleeWeaponData"/> asset that has the durability fields populated.
/// </summary>
[RequireComponent(typeof(PickableObject))]
public class MeleeWeaponDurability : NetworkBehaviour
{
    // ── Configuration ──────────────────────────────────────────────────────────

    [Tooltip("ScriptableObject containing maxDurability and durabilityLossPerHit.")]
    [SerializeField] private MeleeWeaponData weaponData;

    [Tooltip("Played on the owning client when durability reaches zero.")]
    [SerializeField] private AudioClip breakSound;

    [Header("Visual Degradation")]
    [Tooltip("The mesh renderer to swap materials on.")]
    [SerializeField] private MeshRenderer _meshRenderer;

    [Tooltip("Materials for different durability stages. The first element is 100% health, the last is nearly broken.")]
    [SerializeField] private Material[] _damageStages;

    [Header("Breakage Effect")]
    [Tooltip("Prefab containing rigidbodies that spawn when the weapon breaks.")]
    [SerializeField] private GameObject _breakPiecesPrefab;

    [Tooltip("Force applied to broken pieces.")]
    [SerializeField] private float _breakForce = 5f;

    // ── Events ─────────────────────────────────────────────────────────────────

    /// <summary>Raised on the owning client the moment durability hits zero.</summary>
    public UnityAction OnDepleted;

    // ── Internal ───────────────────────────────────────────────────────────────

    private NetworkVariable<int> _currentDurability = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private PickableObject _pickableObject;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        _pickableObject = GetComponent<PickableObject>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _currentDurability.OnValueChanged += OnDurabilityChanged;

        if (IsServer && weaponData != null)
            _currentDurability.Value = weaponData.maxDurability;

        UpdateMaterial();
    }

    public override void OnNetworkDespawn()
    {
        _currentDurability.OnValueChanged -= OnDurabilityChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers a single hit against the weapon's durability.
    /// Must be called from the owning client (or the server for host players).
    /// </summary>
    public void RegisterHit()
    {
        if (IsOwner)
            RegisterHitServerRpc();
    }

    /// <summary>Returns the current durability as a 0–1 normalised value.</summary>
    public float GetDurabilityPercentage()
    {
        if (weaponData == null || weaponData.maxDurability <= 0) return 1f;
        return Mathf.Clamp01((float)_currentDurability.Value / weaponData.maxDurability);
    }

    // ── Server ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Server)]
    private void RegisterHitServerRpc()
    {
        if (weaponData == null) return;

        _currentDurability.Value = Mathf.Max(
            _currentDurability.Value - weaponData.durabilityLossPerHit, 0);

        if (_currentDurability.Value <= 0)
            NotifyDepletedClientRpc();
    }

    // ── Client ─────────────────────────────────────────────────────────────────

    [Rpc(SendTo.Everyone)]
    private void NotifyDepletedClientRpc()
    {
        if (breakSound != null)
            SFXController.Instance.Play(breakSound);

        SpawnBreakPieces();

        if (IsOwner)
        {
            OnDepleted?.Invoke();
        }
    }

    private void SpawnBreakPieces()
    {
        if (_breakPiecesPrefab == null) return;

        GameObject pieces = Instantiate(_breakPiecesPrefab, transform.position, transform.rotation);
        Rigidbody[] rbs = pieces.GetComponentsInChildren<Rigidbody>();

        foreach (Rigidbody rb in rbs)
        {
            Vector3 force = Random.insideUnitSphere * _breakForce;
            rb.AddForce(force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * _breakForce, ForceMode.Impulse);
        }

        // Cleanup pieces after some time
        Destroy(pieces, 10f);
    }

    // ── Callbacks ──────────────────────────────────────────────────────────────

    private void OnDurabilityChanged(int previousValue, int newValue)
    {
        UpdateMaterial();
    }

    private void UpdateMaterial()
    {
        if (_meshRenderer == null || _damageStages == null || _damageStages.Length == 0) return;

        float percentage = GetDurabilityPercentage();
        
        // Map percentage to material index. 1.0 -> 0, 0.0 -> Length-1
        int index = Mathf.Clamp(Mathf.FloorToInt((1f - percentage) * _damageStages.Length), 0, _damageStages.Length - 1);
        
        _meshRenderer.material = _damageStages[index];
    }
}
