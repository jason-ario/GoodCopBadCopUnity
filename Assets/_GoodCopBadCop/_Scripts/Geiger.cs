using UnityEngine;
using System.Collections;

public class GeigerNeedle : MonoBehaviour
{
    [Header("Needle Rotation Ranges (local X only)")]
    public float normalMinX = 40f;
    public float normalMaxX = 60f;

    public float suspiciousMinX = 60f;
    public float suspiciousMaxX = 100f;

    public float infectedMinX = 100f;
    public float infectedMaxX = 140f;

    [Header("Locked Local Rotation Axes")]
    public float lockedY = 0f;
    public float lockedZ = 0f;

    [Header("Motion")]
    public float twitchSpeed = 30f;
    public float smoothSpeed = 12f;

    [Header("Needle Behavior")]
    [Range(0f, 1f)] public float twitchAmount = 0.25f;

    private float displayedRadiation;
    private float targetRadiation;

    private float currentX;
    private float targetX;

    void Start()
    {
        displayedRadiation = 0f;
        targetRadiation = 0f;
        currentX = normalMinX;
        targetX = normalMinX;

        transform.localRotation = Quaternion.Euler(currentX, lockedY, lockedZ);
    }

    void Update()
    {
        displayedRadiation = Mathf.Lerp(displayedRadiation, targetRadiation, Time.deltaTime * smoothSpeed);

        float bandMinX;
        float bandMaxX;
        float bandT;

        if (displayedRadiation < 60f)
        {
            bandMinX = normalMinX;
            bandMaxX = normalMaxX;
            bandT = Mathf.InverseLerp(0f, 60f, displayedRadiation);
        }
        else if (displayedRadiation < 100f)
        {
            bandMinX = suspiciousMinX;
            bandMaxX = suspiciousMaxX;
            bandT = Mathf.InverseLerp(60f, 100f, displayedRadiation);
        }
        else
        {
            bandMinX = infectedMinX;
            bandMaxX = infectedMaxX;
            bandT = Mathf.InverseLerp(100f, 140f, Mathf.Clamp(displayedRadiation, 100f, 140f));
        }

        // Base position from the radiation value
        float baseX = Mathf.Lerp(bandMinX, bandMaxX, bandT);

        // Small twitch layered on top
        float noise = Mathf.PerlinNoise(Time.time * twitchSpeed, 0f);
        float twitch = (noise - 0.5f) * 2f; // -1 to 1

        float bandSize = bandMaxX - bandMinX;
        targetX = baseX + (twitch * bandSize * twitchAmount);
        targetX = Mathf.Clamp(targetX, bandMinX, bandMaxX);

        currentX = Mathf.Lerp(currentX, targetX, Time.deltaTime * smoothSpeed);
        transform.localRotation = Quaternion.Euler(currentX, lockedY, lockedZ);
    }

    public void SetRadiationValue(float radiation)
    {
        targetRadiation = Mathf.Max(0f, radiation);
    }
}