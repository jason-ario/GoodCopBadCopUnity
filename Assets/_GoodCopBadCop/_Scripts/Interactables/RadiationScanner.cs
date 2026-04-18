using System.Collections;
using UnityEngine;

public class RadiationScanner : PickableObject
{
    private Coroutine readingCoroutine;
    private bool isUsing;

    [SerializeField] private float maxDistance = 3f;
    [SerializeField] private GeigerNeedle geigerNeedle;

    [Header("Reading Behavior")]
    [SerializeField] private float baselineRadiation = 5f;
    [SerializeField] private float rampUpSpeed = 90f;
    [SerializeField] private float falloffSpeed = 180f;
    [SerializeField] private float rereadInterval = 0.05f;
    [SerializeField] private float readingJitter = 4f;

    private float currentReading;

    public override void OnStartUse()
    {
        base.OnStartUse();

        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);

        if (readingCoroutine != null)
            StopCoroutine(readingCoroutine);

        readingCoroutine = StartCoroutine(PerformReading());
    }

    public override void OnStopUse()
    {
        base.OnStopUse();

        isUsing = false;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);

        if (readingCoroutine != null)
        {
            StopCoroutine(readingCoroutine);
            readingCoroutine = null;
        }

        currentReading = baselineRadiation;
        geigerNeedle.SetRadiationValue(currentReading);
    }

    private IEnumerator PerformReading()
    {
        currentReading = baselineRadiation;
        geigerNeedle.SetRadiationValue(currentReading);

        while (isUsing)
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

            geigerNeedle.SetRadiationValue(currentReading);

            yield return new WaitForSeconds(rereadInterval);
        }

        readingCoroutine = null;
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