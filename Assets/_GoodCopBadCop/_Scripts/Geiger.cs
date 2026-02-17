using UnityEngine;
using System.Collections;

public class GeigerNeedle : MonoBehaviour
{
    [Header("Rotation References")]
    public Transform minRotation;
    public Transform maxRotation;

    [Header("Motion")]
    public float twitchSpeed = 30f;
    public float smoothSpeed = 10f;

    [Header("Scan Behavior")]
    public float baseIntensity = 0.6f;
    public float scanAggression = 1.2f;
    public float surgeChance = 0.15f;
    public float surgeMultiplier = 1.8f;

    private float intensity = 1f;
    private float currentValue = 0f; // 0 = min, 1 = max
    private float targetValue = 0f;

    private Coroutine scanRoutine;

    void Start()
    {
        if (!minRotation || !maxRotation)
        {
            Debug.LogError("Assign Min and Max rotation transforms.");
            enabled = false;
            return;
        }

        scanRoutine = StartCoroutine(AutoScanRoutine());
    }

    void Update()
    {
        AnimateNeedle();
    }

    void AnimateNeedle()
    {
        // Baseline bias toward danger
        float baseValue = baseIntensity * intensity;

        // Perlin twitch
        float noise = Mathf.PerlinNoise(Time.time * twitchSpeed, 0f);
        float biasedNoise = Mathf.Pow(noise, 0.35f);

        // Blend base + twitch
        targetValue = Mathf.Lerp(baseValue, biasedNoise, 0.6f);
        targetValue = Mathf.Clamp01(targetValue);

        // Smooth
        currentValue = Mathf.Lerp(currentValue, targetValue, Time.deltaTime * smoothSpeed);

        // Interpolate rotation safely
        transform.localRotation = Quaternion.Slerp(
            minRotation.localRotation,
            maxRotation.localRotation,
            currentValue
        );
    }

    IEnumerator AutoScanRoutine()
    {
        while (true)
        {
            float randomBoost = Random.Range(0.7f, scanAggression);

            if (Random.value < surgeChance)
            {
                randomBoost *= surgeMultiplier;
            }

            intensity = randomBoost;

            yield return new WaitForSeconds(Random.Range(0.1f, 0.6f));
        }
    }
}
