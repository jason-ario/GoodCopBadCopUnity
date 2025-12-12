using System;
using TMPro;
using UnityEngine;

public class FPSCounter : MonoBehaviour
{
    private TextMeshProUGUI fpsText;
    private float updateTimer;

    [Header("Update")]
    [SerializeField] private float updateIntervalSeconds = 1f;

    [Header("Thresholds (FPS)")]
    [SerializeField] private float goodFps = 50f;     // >= goodFps => green
    [SerializeField] private float warningFps = 30f;  // >= warningFps => yellow, else red

    [Header("Colors")]
    [SerializeField] private Color goodColor = Color.green;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color badColor = Color.red;

    private void Awake()
    {
        fpsText = GetComponent<TextMeshProUGUI>();
    }

    void Update()
    {
        updateTimer += Time.unscaledDeltaTime;

        if (updateTimer < updateIntervalSeconds)
            return;

        updateTimer = 0f;

        float fps = 1f / Time.unscaledDeltaTime;
        fpsText.text = "FPS: " + fps.ToString("0.0");

        if (fps >= goodFps)
            fpsText.color = goodColor;
        else if (fps >= warningFps)
            fpsText.color = warningColor;
        else
            fpsText.color = badColor;
    }
}