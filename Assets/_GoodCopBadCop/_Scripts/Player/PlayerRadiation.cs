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

        ApplyRadiationDamage();
        CheckRadiationState();
    }

    /// <summary>
    /// Deals damage to the player scaled linearly between the threshold and max radiation.
    /// No damage is applied below the threshold.
    /// </summary>
    private void ApplyRadiationDamage()
    {
        if (playerHealth == null || Normalized <= radiationDamageThreshold)
            return;

        // Remap normalized radiation from [threshold, 1] to [0, 1].
        float damageScale = (Normalized - radiationDamageThreshold) / (1f - radiationDamageThreshold);
        float damage = maxRadiationDamagePerSecond * damageScale * Time.deltaTime;
        playerHealth.TakeDamage(damage, EffectKeys.RadiationTickDamage);
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