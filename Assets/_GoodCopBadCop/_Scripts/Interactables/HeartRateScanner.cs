using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(InternalBattery))]
public class HeartRateScanner : PickableObject
{
    [SerializeField] private TextMeshPro readingText;
    [SerializeField] private float maxDistance = 3f;

    [Header("Reading Behavior")]
    [SerializeField] private float scanDuration = 2.5f;
    [SerializeField] private int readingJitter = 2;

    [Header("Display")]
    [SerializeField] private MeshRenderer screenRenderer;
    [SerializeField] private Color highColor = Color.red;
    [SerializeField] private Color elevatedColor = Color.yellow;
    [SerializeField] private Color normalColor = Color.green;

    [SerializeField] private Color statusTextColor = new Color(0.35f, 0.42f, 0.35f);
    [SerializeField] private Color idleColor = Color.black;
    [SerializeField] private int highHeartRateThreshold = 110;
    [SerializeField] private int elevatedHeartRateThreshold = 90;
    [SerializeField] private int validHeartRateThreshold = 30;
    [SerializeField] private int waveformWidth = 512;
    [SerializeField] private int waveformHeight = 192;

    private InternalBattery internalBattery;
    private Coroutine readingCoroutine;
    private Texture2D waveformTexture;
    private Color32[] waveformPixels;
    private bool hasCompletedReading;
    private SuspectCharacter scanTarget;
    private int sampledBpm;

    protected override void Awake()
    {
        base.Awake();
        internalBattery = GetComponent<InternalBattery>();
        if (screenRenderer == null)
            screenRenderer = FindScreenRenderer();

        CreateWaveformTexture();
        ResetDisplay();
    }

    private void OnDestroy()
    {
        if (waveformTexture != null)
            Destroy(waveformTexture);
    }

    public override void OnStartUse()
    {
        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);

        if (internalBattery.IsBatteryEmpty())
        {
            ShutOff();
            return;
        }

        if (readingCoroutine == null)
        {
            hasCompletedReading = false;
            scanTarget = null;
            sampledBpm = 0;
            ResetDisplay();
            readingCoroutine = StartCoroutine(PerformReading());
        }
    }

    private void Update()
    {
        if (!isUsing)
            return;

        internalBattery.DrainBattery();
        if (internalBattery.IsBatteryEmpty())
            ShutOff();
    }

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);

        if (item.name != "Battery")
            return;

        if (internalBattery.GetBatteryLevel() < 1f)
        {
            internalBattery.Recharge(1f);
            playerInteractionController.pickupController.DestroyEquippedItem();
        }
        else
        {
            Debug.Log("Battery is already full");
        }
    }

    public override void OnStopUse()
    {
        isUsing = false;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        ShutOff();
    }

    private IEnumerator PerformReading()
    {
        float elapsed = 0f;
        ClearWaveform(idleColor);

        while (isUsing && elapsed < scanDuration)
        {
            SuspectCharacter suspect = GetTargetSuspect();
            if (suspect == null)
            {
                elapsed = 0f;
                sampledBpm = 0;
                scanTarget = null;
                if (readingText != null)
                {
                    readingText.text = "AIM";
                    readingText.color = statusTextColor;
                }
                ClearWaveform(idleColor);
                yield return null;
                continue;
            }

            if (suspect != scanTarget)
            {
                scanTarget = suspect;
                elapsed = 0f;
                int jitter = Random.Range(-readingJitter, readingJitter + 1);
                sampledBpm = Mathf.Max(validHeartRateThreshold, suspect.heartRateBpm + jitter);
                ClearWaveform(idleColor);
            }

            elapsed += Time.deltaTime;

            Color scanColor = GetHeartRateColor(sampledBpm);
            DrawWaveform(elapsed / scanDuration, sampledBpm, scanColor);
            if (readingText != null)
            {
                readingText.text = "SCAN";
                readingText.color = GetReadingTextColor(sampledBpm);
            }

            yield return null;
        }

        if (isUsing && sampledBpm > 0)
        {
            UpdateReadingDisplay(sampledBpm);
            hasCompletedReading = true;
        }

        readingCoroutine = null;
    }

    private SuspectCharacter GetTargetSuspect()
    {
        if (Camera.main == null)
            return null;

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            return hit.collider.GetComponentInParent<SuspectCharacter>();

        return null;
    }

    private Color GetHeartRateColor(int bpm)
    {
        if (bpm >= highHeartRateThreshold)
            return highColor;

        return bpm >= elevatedHeartRateThreshold ? elevatedColor : normalColor;
    }

    private Color GetReadingTextColor(int bpm)
    {
        return GetHeartRateColor(bpm);
    }

    private void UpdateReadingDisplay(int bpm)
    {
        if (readingText == null)
            return;

        readingText.text = $"{bpm} BPM";
        readingText.color = GetReadingTextColor(bpm);
    }

    private void ResetDisplay()
    {
        if (readingText != null)
        {
            readingText.text = string.Empty;
            readingText.color = statusTextColor;
        }

        ClearWaveform(idleColor);
    }

    private void ShutOff()
    {
        isUsing = false;
        hasCompletedReading = false;
        StopReading();

        if (readingText != null)
        {
            readingText.text = string.Empty;
        }

        ClearWaveform(Color.black);
    }

    private void StopReading()
    {
        if (readingCoroutine == null)
            return;

        StopCoroutine(readingCoroutine);
        readingCoroutine = null;
    }

    private void CreateWaveformTexture()
    {
        if (screenRenderer == null)
            return;

        waveformWidth = Mathf.Max(16, waveformWidth);
        waveformHeight = Mathf.Max(16, waveformHeight);
        waveformTexture = new Texture2D(waveformWidth, waveformHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        waveformPixels = new Color32[waveformWidth * waveformHeight];
        screenRenderer.material.mainTexture = waveformTexture;
    }

    private void ClearWaveform(Color background)
    {
        if (waveformTexture == null)
            return;

        Color32 fill = background;
        for (int i = 0; i < waveformPixels.Length; i++)
            waveformPixels[i] = fill;

        waveformTexture.SetPixels32(waveformPixels);
        waveformTexture.Apply(false);
    }

    private void DrawWaveform(float progress, int bpm, Color waveformColor)
    {
        if (waveformTexture == null)
            return;

        Color32 background = idleColor;
        Color32 line = waveformColor;
        for (int i = 0; i < waveformPixels.Length; i++)
            waveformPixels[i] = background;

        int drawnColumns = Mathf.Clamp(Mathf.CeilToInt(progress * waveformWidth), 0, waveformWidth);
        float cycles = Mathf.Max(1f, bpm / 60f * scanDuration);
        int previousX = -1;
        int previousY = 0;
        for (int x = 0; x < drawnColumns; x++)
        {
            float phase = x / (float)waveformWidth * cycles;
            int y = Mathf.RoundToInt((0.42f + PulseShape(phase) * 0.38f) * (waveformHeight - 1));
            if (previousX >= 0)
                DrawLine(previousX, previousY, x, y, line);
            else
                DrawDot(x, y, line);

            previousX = x;
            previousY = y;
        }

        waveformTexture.SetPixels32(waveformPixels);
        waveformTexture.Apply(false);
    }

    private float PulseShape(float phase)
    {
        float t = phase - Mathf.Floor(phase);
        float p = Mathf.Exp(-Mathf.Pow((t - 0.12f) / 0.035f, 2f)) * 0.2f;
        float q = -Mathf.Exp(-Mathf.Pow((t - 0.28f) / 0.018f, 2f)) * 0.25f;
        float r = Mathf.Exp(-Mathf.Pow((t - 0.32f) / 0.018f, 2f));
        float s = -Mathf.Exp(-Mathf.Pow((t - 0.37f) / 0.024f, 2f)) * 0.35f;
        float twave = Mathf.Exp(-Mathf.Pow((t - 0.62f) / 0.08f, 2f)) * 0.3f;
        return p + q + r + s + twave;
    }

    private void DrawDot(int x, int y, Color32 color)
    {
        for (int offsetY = -1; offsetY <= 1; offsetY++)
        {
            int pixelY = y + offsetY;
            if (x < 0 || x >= waveformWidth || pixelY < 0 || pixelY >= waveformHeight)
                continue;

            waveformPixels[pixelY * waveformWidth + x] = color;
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1, Color32 color)
    {
        int deltaX = Mathf.Abs(x1 - x0);
        int stepX = x0 < x1 ? 1 : -1;
        int deltaY = -Mathf.Abs(y1 - y0);
        int stepY = y0 < y1 ? 1 : -1;
        int error = deltaX + deltaY;

        while (true)
        {
            DrawDot(x0, y0, color);
            if (x0 == x1 && y0 == y1)
                break;

            int doubledError = error * 2;
            if (doubledError >= deltaY)
            {
                error += deltaY;
                x0 += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y0 += stepY;
            }
        }
    }

    private MeshRenderer FindScreenRenderer()
    {
        Transform screenTransform = transform.Find("Plane");
        if (screenTransform != null && screenTransform.TryGetComponent(out MeshRenderer planeRenderer))
            return planeRenderer;

        screenTransform = transform.Find("Cube (3)");
        if (screenTransform != null && screenTransform.TryGetComponent(out MeshRenderer cubeRenderer))
            return cubeRenderer;

        return GetComponentInChildren<MeshRenderer>(true);
    }
}