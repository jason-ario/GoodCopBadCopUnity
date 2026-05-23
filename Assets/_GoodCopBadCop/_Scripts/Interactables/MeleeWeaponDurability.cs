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
        _pickableObject.OnEquip += ShowDurabilityBar;
        _pickableObject.OnUnEquip += HideDurabilityBar;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _currentDurability.OnValueChanged += OnDurabilityChanged;

        if (IsServer && weaponData != null)
            _currentDurability.Value = weaponData.maxDurability;
    }

    public override void OnNetworkDespawn()
    {
        _currentDurability.OnValueChanged -= OnDurabilityChanged;
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (_pickableObject != null)
        {
            _pickableObject.OnEquip -= ShowDurabilityBar;
            _pickableObject.OnUnEquip -= HideDurabilityBar;
        }

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

    [Rpc(SendTo.Owner)]
    private void NotifyDepletedClientRpc()
    {
        if (breakSound != null)
            SFXController.Instance.Play(breakSound);

        OnDepleted?.Invoke();
    }

    // ── Callbacks ──────────────────────────────────────────────────────────────

    private void OnDurabilityChanged(int previousValue, int newValue)
    {
        // Only update the bar while it is visible (i.e. the weapon is equipped by this player).
        if (PlayerUI.Instance != null && PlayerUI.Instance.BatteryBar.gameObject.activeSelf)
            PlayerUI.Instance.BatteryBar.UpdateBar(GetDurabilityPercentage());
    }

    private void ShowDurabilityBar()
    {
        if (PlayerUI.Instance == null) return;
        PlayerUI.Instance.BatteryBar.Show();
        PlayerUI.Instance.BatteryBar.UpdateBar(GetDurabilityPercentage());
    }

    private void HideDurabilityBar()
    {
        if (PlayerUI.Instance != null)
            PlayerUI.Instance.BatteryBar.Hide();
    }
}
