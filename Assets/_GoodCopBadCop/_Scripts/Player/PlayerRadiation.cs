using UnityEngine;
using UnityEngine.Events;

public class PlayerRadiation : MonoBehaviour
{
    [Header("Radiation")]
    [SerializeField] private float maxRadiation = 100f;
    [SerializeField] private float currentRadiation = 0f;
    [SerializeField] private float passiveRadiationPerSecond = 0.15f;

    [Header("Pills")]
    [SerializeField] private float pillReductionAmount = 30f;
    [SerializeField] private float pillReductionDuration = 6f;

    [Header("Death")]
    [SerializeField] private bool dieAtMaxRadiation = true;

    public UnityEvent<float, float> OnRadiationChanged; // current, max
    public UnityEvent OnRadiationCritical;
    public UnityEvent OnRadiationDeath;

    private bool isTakingPill;
    private float pillTimer;
    private float pillDrainPerSecond;
    private bool hasTriggeredCritical;

    public float CurrentRadiation => currentRadiation;
    public float MaxRadiation => maxRadiation;
    public float Normalized => currentRadiation / maxRadiation;
    [SerializeField] private GameObject hurtVignette;

    private void Update()
    {
        AddRadiation(passiveRadiationPerSecond * Time.deltaTime);

        if (isTakingPill)
        {
            pillTimer -= Time.deltaTime;
            RemoveRadiation(pillDrainPerSecond * Time.deltaTime);

            if (pillTimer <= 0f)
                isTakingPill = false;
        }

        CheckRadiationState();
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