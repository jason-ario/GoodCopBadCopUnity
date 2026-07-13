using System.Collections;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(InternalBattery))]
public class HeartRateScanner : PickableObject
{
    [SerializeField] private TextMeshPro readingText;
    [SerializeField] private float maxDistance = 3f;

    [Header("Reading Behavior")]
    [SerializeField] private int idleHeartRateBpm = 0;
    [SerializeField] private float rampSpeed = 180f;
    [SerializeField] private float falloffSpeed = 240f;
    [SerializeField] private float rereadInterval = 0.08f;
    [SerializeField] private int readingJitter = 2;

    [Header("Display")]
    [SerializeField] private MeshRenderer screenRenderer;
    [SerializeField] private Color highColor = Color.red;
    [SerializeField] private Color normalColor = Color.green;
    [SerializeField] private Color idleColor = Color.black;
    [SerializeField] private float screenAnimationSpeed = 0.35f;
    [SerializeField] private int highHeartRateThreshold = 110;
    [SerializeField] private int validHeartRateThreshold = 30;

    private InternalBattery internalBattery;
    private Coroutine readingCoroutine;
    private float currentReading;

    protected override void Awake()
    {
        base.Awake();
        internalBattery = GetComponent<InternalBattery>();
        if (screenRenderer == null)
            screenRenderer = FindScreenRenderer();

        ResetDisplay();
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

        if (readingCoroutine != null)
            StopCoroutine(readingCoroutine);

        readingCoroutine = StartCoroutine(PerformReading());
    }

    private void Update()
    {
        if (!isUsing)
            return;

        internalBattery.DrainBattery();

        if (internalBattery.IsBatteryEmpty())
            ShutOff();

        AnimateScreen();
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
        StopReading();
        ResetDisplay();
    }

    private IEnumerator PerformReading()
    {
        currentReading = idleHeartRateBpm;
        ResetDisplay();

        while (isUsing)
        {
            SuspectCharacter suspect = GetTargetSuspect();

            if (suspect != null)
            {
                int jitter = Random.Range(-readingJitter, readingJitter + 1);
                int targetReading = Mathf.Max(validHeartRateThreshold, suspect.heartRateBpm + jitter);

                currentReading = Mathf.MoveTowards(
                    currentReading,
                    targetReading,
                    rampSpeed * Time.deltaTime);

                UpdateReadingDisplay(Mathf.RoundToInt(currentReading));
            }
            else
            {
                currentReading = Mathf.MoveTowards(
                    currentReading,
                    idleHeartRateBpm,
                    falloffSpeed * Time.deltaTime);

                ResetDisplay();
            }

            yield return new WaitForSeconds(rereadInterval);
        }

        readingCoroutine = null;
    }

    private SuspectCharacter GetTargetSuspect()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            return hit.collider.GetComponentInParent<SuspectCharacter>();

        return null;
    }

    private void UpdateReadingDisplay(int bpm)
    {
        if (readingText != null)
            readingText.text = $"{bpm} BPM";

        UpdateScreenColor(bpm >= highHeartRateThreshold ? highColor : normalColor);
    }

    private void ResetDisplay()
    {
        if (readingText != null)
            readingText.text = "---";

        UpdateScreenColor(idleColor);
    }

    private void ShutOff()
    {
        StopReading();

        if (readingText != null)
            readingText.text = "off";

        UpdateScreenColor(idleColor);
    }

    private void StopReading()
    {
        if (readingCoroutine == null)
            return;

        StopCoroutine(readingCoroutine);
        readingCoroutine = null;
    }

    private void UpdateScreenColor(Color color)
    {
        if (screenRenderer != null)
            screenRenderer.material.color = color;
    }

    private void AnimateScreen()
    {
        if (screenRenderer == null || screenAnimationSpeed <= 0f)
            return;

        Material material = screenRenderer.material;
        Vector2 offset = material.mainTextureOffset;
        offset.x = Mathf.Repeat(offset.x + screenAnimationSpeed * Time.deltaTime, 1f);
        material.mainTextureOffset = offset;
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
