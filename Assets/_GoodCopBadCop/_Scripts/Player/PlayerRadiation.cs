using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class PlayerRadiation : NetworkBehaviour
{
    [Header("Radiation")]
    [SerializeField] private float maxRadiation = 100f;
    [SerializeField] private float passiveRadiationPerSecond = 0.15f;

    [Header("Pills")]
    [SerializeField] private float pillReductionAmount = 30f;
    [SerializeField] private float pillReductionDuration = 6f;

    [Header("Radiation Damage")]
    [Tooltip("Radiation level (0-1) above which the player starts taking damage.")]
    [SerializeField] private float radiationDamageThreshold = 0.75f;
    [Tooltip("Maximum health damage per second dealt at 100% radiation.")]
    [SerializeField] private float maxRadiationDamagePerSecond = 5f;

    [Header("Radiation Tick Feedback")]
    [Tooltip("Radiation gain rate (units/sec) above which the distinct radiation-tick feedback " +
             "(vignette pulse / camera kick / tick sound) plays on damage. Below this rate, damage " +
             "still applies but uses the default damage feedback instead of the tick.")]
    [SerializeField] private float tickFeedbackRateThreshold = 0.5f;
    [Tooltip("EMA time constant (seconds) used to smooth the measured radiation gain rate.")]
    [SerializeField] private float rateSmoothingWindow = 0.4f;

    private float _previousRadiationForRate;
    private float _smoothedRadiationRate;

    /// <summary>Smoothed radiation gain rate, in units/sec. Reflects current velocity, not accrued total.</summary>
    public float RadiationRate => _smoothedRadiationRate;

    [Header("Death")]
    [SerializeField] private bool dieAtMaxRadiation = true;

    public UnityEvent<float, float> OnRadiationChanged; // current, max
    public UnityEvent OnRadiationCritical;
    public UnityEvent OnRadiationDeath;

    private bool isTakingPill;
    private float pillTimer;
    private float pillDrainPerSecond;
    private bool hasTriggeredCritical;

    private PlayerHealth playerHealth;
    private PlayerInstance playerInstance;

    /// <summary>
    /// Server-authoritative accrued radiation, replicated to every client via NetworkVariable.
    /// Previously this was a plain field only ever mutated inside Update() — which early-returns
    /// on every non-host client (see Update() below) — so a non-host client's OWN copy of their
    /// own radiation never advanced locally: their radiation bar/HUD stayed frozen near zero even
    /// while the server's authoritative copy climbed and correctly applied real damage via
    /// PlayerHealth. That looked exactly like "taking damage rapidly for no reason" because the
    /// only thing that was wrong was the display, not the damage. Being a NetworkVariable, its
    /// current value (and every subsequent change) is delivered correctly to every client,
    /// including late joiners, via ordinary replication.
    /// </summary>
    private readonly NetworkVariable<float> _networkRadiation = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public float CurrentRadiation => _networkRadiation.Value;
    public float MaxRadiation => maxRadiation;
    public float Normalized => _networkRadiation.Value / maxRadiation;
    [SerializeField] private GameObject hurtVignette;

    /// <summary>When true, passive radiation gain and radiation damage are paused. Server-side only.</summary>
    public bool IsInvincible { get; set; }

    /// <summary>
    /// Scales all incoming radiation accumulation (passive and hotspot).
    /// Set to a value less than 1 to slow radiation build-up (e.g. 0.2 while wearing the mask).
    /// Server-side only - only meaningful on the host since radiation runs there.
    /// </summary>
    public float RadiationMultiplier { get; set; } = 1f;

    private void Awake()
    {
        playerHealth = GetComponent<PlayerHealth>();
        playerInstance = GetComponent<PlayerInstance>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _networkRadiation.OnValueChanged += HandleRadiationChanged;

        // Sync initial state immediately, including for late-joining clients.
        OnRadiationChanged?.Invoke(_networkRadiation.Value, maxRadiation);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _networkRadiation.OnValueChanged -= HandleRadiationChanged;
    }

    private void Update()
    {
        // Only the server drives radiation damage to avoid per-frame ServerRpc spam.
        if (!IsServer)
            return;

        if (IsInvincible)
            return;

        // While this player is locked inside a scripted (or classic) dialogue cutscene,
        // radiation should not accrue, drain, or damage the player at all — mirrors the
        // cutscene guard used by MutantEnemy/MutantAttackHitbox for combat.
        // IsInCutscene is owner-written and replicated to the server, so this check is
        // server-authoritative even though the flag originates on the owning client.
        if (playerInstance != null && playerInstance.IsInCutscene)
            return;

        ApplyAddRadiationServer(passiveRadiationPerSecond * RadiationMultiplier * Time.deltaTime);

        if (isTakingPill)
        {
            pillTimer -= Time.deltaTime;
            ApplyRemoveRadiationServer(pillDrainPerSecond * Time.deltaTime);

            if (pillTimer <= 0f)
                isTakingPill = false;
        }

        UpdateRadiationRate();
        ApplyRadiationDamage();
    }

    /// <summary>
    /// Tracks how fast radiation is currently being gained (units/sec), independent of the
    /// accrued total. Pill drain (a decrease) is clamped to zero so it never registers as a gain.
    /// </summary>
    private void UpdateRadiationRate()
    {
        float currentRadiation = _networkRadiation.Value;
        float instantRate = (currentRadiation - _previousRadiationForRate) / Time.deltaTime;
        _previousRadiationForRate = currentRadiation;

        instantRate = Mathf.Max(0f, instantRate);

        float alpha = Mathf.Clamp01(Time.deltaTime / Mathf.Max(rateSmoothingWindow, 0.001f));
        _smoothedRadiationRate = Mathf.Lerp(_smoothedRadiationRate, instantRate, alpha);
    }

    /// <summary>
    /// Deals damage to the player scaled linearly between the threshold and max radiation.
    /// No damage is applied below the threshold. The distinct radiation-tick feedback (vignette
    /// pulse / camera kick / tick sound) only plays while radiation is actively climbing fast
    /// (<see cref="RadiationRate"/> above <see cref="tickFeedbackRateThreshold"/>) — not merely
    /// because the accrued total is above the damage threshold.
    /// </summary>
    private void ApplyRadiationDamage()
    {
        if (playerHealth == null || Normalized <= radiationDamageThreshold)
            return;

        // Remap normalized radiation from [threshold, 1] to [0, 1].
        float damageScale = (Normalized - radiationDamageThreshold) / (1f - radiationDamageThreshold);
        float damage = maxRadiationDamagePerSecond * damageScale * Time.deltaTime;

        string effectKey = _smoothedRadiationRate >= tickFeedbackRateThreshold
            ? EffectKeys.RadiationTickDamage
            : EffectKeys.DefaultPlayerDamage;

        playerHealth.TakeDamage(damage, effectKey);
    }

    /// <summary>Adds radiation. Can be called from any client — routes through a ServerRpc when not on the server.</summary>
    public void AddRadiation(float amount)
    {
        if (amount <= 0f) return;

        if (IsServer)
            ApplyAddRadiationServer(amount);
        else
            AddRadiationServerRpc(amount);
    }

    /// <summary>Removes radiation. Can be called from any client — routes through a ServerRpc when not on the server.</summary>
    public void RemoveRadiation(float amount)
    {
        if (amount <= 0f) return;

        if (IsServer)
            ApplyRemoveRadiationServer(amount);
        else
            RemoveRadiationServerRpc(amount);
    }

    /// <summary>
    /// Clears accrued radiation back to zero and resets critical-state tracking.
    /// Intended for server-side use (e.g. at the start of a new day) — does not
    /// touch <see cref="isTakingPill"/>/<see cref="pillTimer"/> pill state, which
    /// will simply resume draining against the now-zeroed radiation on its own.
    /// </summary>
    public void ResetRadiation()
    {
        if (IsServer)
            ApplyResetRadiationServer();
        else
            ResetRadiationServerRpc();
    }

    public void TakeRadiationPill()
    {
        if (IsServer)
            ApplyTakeRadiationPillServer();
        else
            TakeRadiationPillServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddRadiationServerRpc(float amount) => ApplyAddRadiationServer(amount);

    [ServerRpc(RequireOwnership = false)]
    private void RemoveRadiationServerRpc(float amount) => ApplyRemoveRadiationServer(amount);

    [ServerRpc(RequireOwnership = false)]
    private void ResetRadiationServerRpc() => ApplyResetRadiationServer();

    [ServerRpc(RequireOwnership = false)]
    private void TakeRadiationPillServerRpc() => ApplyTakeRadiationPillServer();

    private void ApplyAddRadiationServer(float amount)
    {
        if (amount <= 0f) return;
        _networkRadiation.Value = Mathf.Clamp(_networkRadiation.Value + amount, 0f, maxRadiation);
    }

    private void ApplyRemoveRadiationServer(float amount)
    {
        if (amount <= 0f) return;
        _networkRadiation.Value = Mathf.Clamp(_networkRadiation.Value - amount, 0f, maxRadiation);
    }

    private void ApplyResetRadiationServer()
    {
        hasTriggeredCritical = false;
        _previousRadiationForRate = 0f;
        _smoothedRadiationRate = 0f;
        _networkRadiation.Value = 0f;
    }

    private void ApplyTakeRadiationPillServer()
    {
        isTakingPill = true;
        pillTimer = pillReductionDuration;
        pillDrainPerSecond = pillReductionAmount / pillReductionDuration;
    }

    /// <summary>
    /// Fires on every client whenever <see cref="_networkRadiation"/> changes, including the
    /// initial replication for late joiners. Drives the local UI event and the one-shot
    /// critical/death triggers — replacing logic that previously ran only inside the
    /// server-gated Update(), which meant non-host clients never saw their own critical/death
    /// feedback fire at all.
    /// </summary>
    private void HandleRadiationChanged(float previousValue, float newValue)
    {
        OnRadiationChanged?.Invoke(newValue, maxRadiation);

        float normalized = newValue / maxRadiation;

        if (normalized < 0.75f)
            hasTriggeredCritical = false;

        if (!hasTriggeredCritical && normalized >= 0.75f)
        {
            hasTriggeredCritical = true;
            OnRadiationCritical?.Invoke();
        }

        if (dieAtMaxRadiation && newValue >= maxRadiation)
        {
            OnRadiationDeath?.Invoke();
        }
    }
}