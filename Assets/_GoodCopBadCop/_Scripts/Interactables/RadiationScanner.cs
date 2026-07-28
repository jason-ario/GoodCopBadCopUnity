using System.Collections;
using UnityEngine;
using UnityEngine.Animations;

[RequireComponent(typeof(InternalBattery))]
public class RadiationScanner : PickableObject
{
    private Coroutine readingCoroutine;

    [SerializeField] private float maxDistance = 3f;
    [SerializeField] private GeigerNeedle geigerNeedle;

    [Header("Reading Behavior")]
    [SerializeField] private float baselineRadiation = 5f;
    [SerializeField] private float rampUpSpeed = 90f;
    [SerializeField] private float falloffSpeed = 180f;
    [SerializeField] private float rereadInterval = 0.05f;
    [SerializeField] private float readingJitter = 4f;

    [Header("Radiation Zone Chaos")]
    [Tooltip("The wielder's radiation gain rate (units/sec) must be at or above this while " +
             "outside any RadiationSafeZone for the scanner to go haywire.")]
    [SerializeField] private float chaosRadiationRateThreshold = 0.3f;
    [Tooltip("Random target readings while in chaos mode are rolled between these bounds.")]
    [SerializeField] private float chaosMinReading = 90f;
    [SerializeField] private float chaosMaxReading = 170f;
    [Tooltip("Extra jitter layered on top of each chaotic target reading.")]
    [SerializeField] private float chaosJitter = 25f;
    [Tooltip("How fast the needle chases each chaotic target reading (higher = snappier spikes).")]
    [SerializeField] private float chaosRampSpeed = 600f;
    [Tooltip("How often a new chaotic target reading is rolled while in danger.")]
    [SerializeField] private float chaosRerollInterval = 0.06f;

    InternalBattery internalBattery;
    private PlayerRadiation wielderRadiation;
    private RadiationSafeZone[] safeZonesInScene;

    private float currentReading;
    private float chaosTargetReading;
    private float chaosRerollTimer;

    protected override void Awake()
    {
        base.Awake();
        internalBattery = GetComponent<InternalBattery>();
    }

    public override void OnStartUse()
    {
        base.OnStartUse();

        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);

        wielderRadiation = playerPickupController.GetComponent<PlayerRadiation>();
        safeZonesInScene = FindObjectsByType<RadiationSafeZone>(FindObjectsSortMode.None);

        if (readingCoroutine != null)
            StopCoroutine(readingCoroutine);

        readingCoroutine = StartCoroutine(PerformReading());
    }
    
    private void Update()
    {
        if (isUsing)
        {
            internalBattery.DrainBattery();
            if (internalBattery.IsBatteryEmpty())
            {
                ShutOff();
            }
        }
    }

    void ShutOff()
    {
        if (readingCoroutine != null)
        {
            StopCoroutine(readingCoroutine);
            readingCoroutine = null;
        }

        currentReading = baselineRadiation;
        geigerNeedle.SetRadiationValue(currentReading);
    }

    public override void OnStopUse()
    {
        base.OnStopUse();

        isUsing = false;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        
        ShutOff();
    }

    private IEnumerator PerformReading()
    {
        currentReading = baselineRadiation;
        geigerNeedle.SetRadiationValue(currentReading);
        chaosRerollTimer = 0f;

        while (isUsing)
        {
            if (IsWielderInRadiationDanger())
            {
                UpdateChaosReading();
            }
            else
            {
                SuspectCharacter suspect = GetTargetSuspect();

                if (suspect != null)
                {
                    float targetReading = suspect.radiationAmount + Random.Range(-readingJitter, readingJitter);
                    targetReading = Mathf.Max(baselineRadiation, targetReading);

                    currentReading = Mathf.MoveTowards(
                        currentReading,
                        targetReading,
                        rampUpSpeed * Time.deltaTime
                    );
                }
                else
                {
                    currentReading = Mathf.MoveTowards(
                        currentReading,
                        baselineRadiation,
                        falloffSpeed * Time.deltaTime
                    );
                }
            }

            geigerNeedle.SetRadiationValue(currentReading);

            yield return new WaitForSeconds(rereadInterval);
        }

        readingCoroutine = null;
    }

    /// <summary>
    /// Returns true if the wielding player is currently gaining radiation fast enough to be
    /// considered "in a radiation filled environment" (hotspot/off-trail bonus on top of
    /// passive gain) AND is not standing inside a <see cref="RadiationSafeZone"/>.
    /// </summary>
    private bool IsWielderInRadiationDanger()
    {
        if (wielderRadiation == null)
            return false;

        if (wielderRadiation.RadiationRate < chaosRadiationRateThreshold)
            return false;

        if (safeZonesInScene != null)
        {
            foreach (RadiationSafeZone zone in safeZonesInScene)
            {
                if (zone != null && zone.Contains(wielderRadiation))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Drives the needle with rapidly-rerolled, jittery random readings to simulate the
    /// scanner going haywire while the wielder is exposed outside a safe zone.
    /// </summary>
    private void UpdateChaosReading()
    {
        chaosRerollTimer -= rereadInterval;
        if (chaosRerollTimer <= 0f)
        {
            chaosTargetReading = Random.Range(chaosMinReading, chaosMaxReading);
            chaosRerollTimer = chaosRerollInterval;
        }

        float jitteredTarget = chaosTargetReading + Random.Range(-chaosJitter, chaosJitter);

        currentReading = Mathf.MoveTowards(
            currentReading,
            jitteredTarget,
            chaosRampSpeed * Time.deltaTime
        );
    }

    private SuspectCharacter GetTargetSuspect()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
        {
            return hit.collider.GetComponentInParent<SuspectCharacter>();
        }

        return null;
    }
}