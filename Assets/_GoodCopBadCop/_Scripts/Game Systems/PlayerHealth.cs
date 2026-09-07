using GoodCopBadCop.Effects;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Networked player health component.
/// The server is the sole writer of <see cref="_networkHealth"/>; all clients receive
/// the updated value via <see cref="NetworkVariable{T}"/> replication and fire the
/// local <see cref="OnHealthChanged"/> / <see cref="OnDeath"/> events so UI stays in sync.
/// </summary>
public class PlayerHealth : NetworkBehaviour
{

    // Configuration

    private const float DefaultMaxHealth = 100f;

    public float MaxHealth => DefaultMaxHealth;

    [Header("Hit Effects")]
    [Tooltip("Particle prefab (e.g. blood spray) instantiated on all clients at the point of " +
             "impact whenever this player takes damage — mirrors MutantEnemy/SuspectCharacter's " +
             "hit-particle behaviour. Plays whether the hit kills the player or not. Optional.")]
    [SerializeField] private GameObject _hitParticlePrefab;

    [Tooltip("Random hurt/pain sound played on all clients (at the hit point) whenever this " +
             "player takes damage. Optional.")]
    [SerializeField] private AudioClip[] _hurtSounds;


    // Events

    /// <summary>Fired on every client whenever health changes.</summary>
    public UnityAction OnHealthChanged;

    /// <summary>Fired on every client when health reaches zero for the first time.</summary>
    public UnityAction OnDeath;

    /// <summary>Fired on every client when health is reset and the player is no longer dead.</summary>
    public UnityAction OnRespawn;


    // Networked State

    private readonly NetworkVariable<float> _networkHealth = new NetworkVariable<float>(
        DefaultMaxHealth,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> _networkIsDead = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<FixedString64Bytes> _lastHealthEffectKey = new NetworkVariable<FixedString64Bytes>(
        EffectKeys.DefaultPlayerDamage,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);


    // Local accessors

    /// <summary>Current health, readable on all clients.</summary>
    public float Health => _networkHealth.Value;

    /// <summary>Whether this player is dead, readable on all clients.</summary>
    public bool IsDead => _networkIsDead.Value;

    /// <summary>The gameplay effect key associated with the latest health mutation.</summary>
    public string LastHealthEffectKey => _lastHealthEffectKey.Value.ToString();

    /// <summary>When true, all incoming damage is ignored. Server-side only.</summary>
    public bool IsInvincible { get; set; }

    private PlayerInstance _playerInstance;


    // Lifecycle

    private void Awake()
    {
        _playerInstance = GetComponent<PlayerInstance>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkHealth.OnValueChanged += HandleHealthChanged;
        _networkIsDead.OnValueChanged += HandleDeadChanged;

        // Sync initial state immediately on late-joining clients.
        OnHealthChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _networkHealth.OnValueChanged -= HandleHealthChanged;
        _networkIsDead.OnValueChanged -= HandleDeadChanged;
    }


    // Public API

    /// <summary>
    /// Reduces health by the given amount.
    /// Can be called from any client - routes through a ServerRpc when not on the server.
    /// </summary>
    public void TakeDamage(float damage, string effectKey)
    {
        TakeDamage(damage, effectKey, transform.position);
    }

    /// <summary>
    /// Reduces health by the given amount, spawning the hit particle/sound at
    /// <paramref name="hitPoint"/> (e.g. the exact spot a shovel swing or gunshot connected)
    /// instead of the player's own position.
    /// Can be called from any client - routes through a ServerRpc when not on the server.
    /// </summary>
    public void TakeDamage(float damage, string effectKey, Vector3 hitPoint)
    {
        if (IsServer)
            ApplyDamageServer(damage, effectKey, hitPoint);
        else
            TakeDamageServerRpc(damage, effectKey, hitPoint);
    }

    /// <summary>Restores health by the given amount. Has no effect while the player is dead.</summary>
    public void Heal(float healAmount)
    {
        Heal(healAmount, EffectKeys.PlayerHeal);
    }

    public void Heal(float healAmount, string effectKey)
    {
        if (IsServer)
            ApplyHealServer(healAmount, effectKey);
        else
            HealServerRpc(healAmount, effectKey);
    }

    /// <summary>Resets health to max and clears the dead state.</summary>
    public void ResetHealth()
    {
        if (IsServer)
            ApplyResetServer();
        else
            ResetHealthServerRpc();
    }


    // ServerRpcs

    [ServerRpc(RequireOwnership = false)]
    private void TakeDamageServerRpc(float damage, FixedString64Bytes effectKey, Vector3 hitPoint)
    {
        ApplyDamageServer(damage, effectKey.ToString(), hitPoint);
    }

    [ServerRpc(RequireOwnership = false)]
    private void HealServerRpc(float healAmount, FixedString64Bytes effectKey)
    {
        ApplyHealServer(healAmount, effectKey.ToString());
    }

    [ServerRpc(RequireOwnership = false)]
    private void ResetHealthServerRpc()
    {
        ApplyResetServer();
    }


    // Server-only logic

    private void ApplyDamageServer(float damage, string effectKey, Vector3 hitPoint)
    {
        if (_networkIsDead.Value)
            return;

        if (IsInvincible)
            return;

        // Players locked into (or otherwise participating in) a scripted dialogue must be
        // fully immune to damage from every source (mutants, radiation, fire, etc.) — this
        // is a server-authoritative backstop in addition to the mutant-side targeting/hit
        // checks in MutantEnemy and MutantAttackHitbox.
        if (_playerInstance != null && _playerInstance.IsInCutscene)
            return;

        _lastHealthEffectKey.Value = ToNetworkEffectKey(effectKey, EffectKeys.DefaultPlayerDamage);
        _networkHealth.Value = UnityEngine.Mathf.Clamp(_networkHealth.Value - damage, 0f, DefaultMaxHealth);

        // Blood/impact feedback plays on every real hit, whether or not it's the killing blow —
        // mirrors MutantEnemy.TakeDamage / SuspectCharacter.TakeDamage.
        SpawnHitEffectClientRpc(hitPoint);

        if (_networkHealth.Value <= 0f)
            _networkIsDead.Value = true;
    }

    private void ApplyHealServer(float healAmount, string effectKey)
    {
        if (_networkIsDead.Value)
            return;

        _lastHealthEffectKey.Value = ToNetworkEffectKey(effectKey, EffectKeys.PlayerHeal);
        _networkHealth.Value = UnityEngine.Mathf.Clamp(_networkHealth.Value + healAmount, 0f, DefaultMaxHealth);
    }

    private void ApplyResetServer()
    {
        _networkIsDead.Value = false;
        _lastHealthEffectKey.Value = EffectKeys.PlayerHeal;
        _networkHealth.Value = DefaultMaxHealth;
    }

    private static FixedString64Bytes ToNetworkEffectKey(string effectKey, string fallback)
    {
        string safeKey = string.IsNullOrWhiteSpace(effectKey) ? fallback : effectKey;
        return safeKey.Length > 63 ? safeKey.Substring(0, 63) : safeKey;
    }


    // Effects

    /// <summary>
    /// Spawns the hit particle (e.g. blood spray) and plays a random hurt sound at
    /// <paramref name="hitPoint"/> on all clients. Fires on every damage-dealing hit regardless
    /// of whether the player survives it — mirrors MutantEnemy/SuspectCharacter's hit feedback.
    /// </summary>
    [ClientRpc]
    private void SpawnHitEffectClientRpc(Vector3 hitPoint)
    {
        if (_hitParticlePrefab != null)
        {
            GameObject fx = Instantiate(_hitParticlePrefab, hitPoint, Quaternion.identity);
            if (fx.GetComponentInChildren<AutoDestroy>() == null)
                fx.AddComponent<AutoDestroy>();
        }

        if (_hurtSounds != null && _hurtSounds.Length > 0)
        {
            AudioClip clip = _hurtSounds[UnityEngine.Random.Range(0, _hurtSounds.Length)];
            if (clip != null)
                SFXController.Instance?.PlayAtPosition(clip, hitPoint);
        }
    }


    // NetworkVariable callbacks

    private void HandleHealthChanged(float previousValue, float newValue)
    {
        OnHealthChanged?.Invoke();
    }

    private void HandleDeadChanged(bool previousValue, bool newValue)
    {
        if (newValue)
        {
            OnHealthChanged?.Invoke();
            OnDeath?.Invoke();

            // Analytics: track player deaths — which day, and to what/whom (the effect key
            // that dealt the killing blow, e.g. "MutantMeleeDamage", "RadiationTickDamage").
            // Guarded by IsOwner so each death is only reported once, by the player who died,
            // rather than by every remote client that also observes this NetworkVariable.
            if (IsOwner)
            {
                int day = CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;
                string killer = LastHealthEffectKey;

                GameAnalyticsSDK.GameAnalytics.NewDesignEvent($"Player:Death:{day}:{killer}");
                GameAnalyticsSDK.GameAnalytics.NewProgressionEvent(
                    GameAnalyticsSDK.GAProgressionStatus.Fail,
                    $"Day{day}",
                    killer);
            }
        }
        else if (previousValue && !newValue)
        {
            OnHealthChanged?.Invoke();
            OnRespawn?.Invoke();
        }
    }
}
