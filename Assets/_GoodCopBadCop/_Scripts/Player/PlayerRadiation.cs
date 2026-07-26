using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class PlayerRadiation : NetworkBehaviour
{
    [Header("Radiation")]
    [SerializeField] private float maxRadiation = 100f;
    [SerializeField] private float currentRadiation = 0f;
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

    public float CurrentRadiation => currentRadiation;
    public float MaxRadiation => maxRadiation;
    public float Normalized => currentRadiation / maxRadiation;
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

    private void Update()
    {
        // Only the server drives radiation damage to avoid per-frame ServerRpc spam.
        // Cosmetic radiation state (vignette, UI) should subscribe to a networked variable
        // if you want it to display on all clients in the future.
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

        AddRadiation(passiveRadiationPerSecond * RadiationMultiplier * Time.deltaTime);

        if (isTakingPill)
        {
            pillTimer -= Time.deltaTime;
            RemoveRadiation(pillDrainPerSecond * Time.deltaTime);

            if (pillTimer <= 0f)
                isTakingPill = false;
        }

        UpdateRadiationRate();
        ApplyRadiationDamage();
        CheckRadiationState();
    }

    /// <summary>
    /// Tracks how fast radiation is currently being gained (units/sec), independent of the
    /// accrued total. Pill drain (a decrease) is clamped to zero so it never registers as a gain.
    /// </summary>
    private void UpdateRadiationRate()
    {
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

    public void AddRadiation(float amount)
    {
        if (amount <= 0f) return;

        currentRadiation = Mathf.Clamp(currentRadiation + amount, 0f, maxRadiation);
        OnRadiationChanged?.Invoke(currentRadiation, maxRadiation);
    }

    public void RemoveRadiation(float amount)
    {
        if (amount <= 0f) return;

        currentRadiation = Mathf.Clamp(currentRadiation - amount, 0f, maxRadiation);
        OnRadiationChanged?.Invoke(currentRadiation, maxRadiation);

        if (Normalized < 0.75f)
            hasTriggeredCritical = false;
    }

    public void TakeRadiationPill()
    {
        isTakingPill = true;
        pillTimer = pillReductionDuration;
        pillDrainPerSecond = pillReductionAmount / pillReductionDuration;
    }

    private void CheckRadiationState()
    {
        if (!hasTriggeredCritical && Normalized >= 0.75f)
        {
            hasTriggeredCritical = true;
            OnRadiationCritical?.Invoke();
        }

        if (dieAtMaxRadiation && currentRadiation >= maxRadiation)
        {
            OnRadiationDeath?.Invoke();
        }
    }
}