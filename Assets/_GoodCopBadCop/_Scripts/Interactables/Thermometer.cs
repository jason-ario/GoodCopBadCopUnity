using System;
using System.Collections;
using GoodCopBadCop.RoomSystem;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(InternalBattery))]
public class Thermometer : PickableObject
{
    [SerializeField] TextMeshPro thermometerText;
    [SerializeField] private float maxDistance = 3f;
    
    private Coroutine readingCoroutine;
    private float currentReading;
    private const float TargetBaseTemp = 45.5f;
    private const float NormalBaseTemp = 36.5f;
    private const float NormalJitterRange = 0.3f;
    
    [SerializeField] private MeshRenderer screenRenderer;
    [SerializeField] private Color highColor;
    [SerializeField] private Color normalColor;
    [SerializeField] private Color idleColor;
    private InternalBattery internalBattery;

    [VContainer.Inject] private IRoomService roomService;
    protected override void Awake()
    {
        base.Awake();
        internalBattery = GetComponent<InternalBattery>();
    }

    public override void OnStartUse()
    {
        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", true);

        if (internalBattery.IsBatteryEmpty())
        {
            return;
        }
        
        if (readingCoroutine != null) StopCoroutine(readingCoroutine);
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

    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        if (item.name == "Battery")
        {
            if (internalBattery.GetBatteryLevel() < 1)
            {
                internalBattery.Recharge(1);
                playerInteractionController.pickupController.DestroyEquippedItem();
            }
            else
            {
                Debug.Log("Battery is already full");
            }
        }
    }

    public void FakeNormalReading()
    {
        if (readingCoroutine != null) StopCoroutine(readingCoroutine);
        readingCoroutine = StartCoroutine(FakeNormalReadingRoutine());
    }

    private IEnumerator FakeNormalReadingRoutine()
    {
        float elapsed = 0f;
        float duration = 1f;
        float startTemp = 0f;
        float targetTemp = 36.5f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float jitter = UnityEngine.Random.Range(-0.5f, 0.5f) * (1f - t);
            currentReading = Mathf.Lerp(startTemp, targetTemp, t) + jitter;
            thermometerText.text = Mathf.RoundToInt(currentReading).ToString() + "°";
            SetColorFromTemp(currentReading);
            yield return null;
        }

        // Settle on final value
        currentReading = targetTemp;
        thermometerText.text = Mathf.RoundToInt(currentReading).ToString() + "°";
        SetColorFromTemp(currentReading);
        readingCoroutine = null;
    }

    public override void OnStopUse()
    {
        isUsing = false;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingTool", false);
        
        if (readingCoroutine != null)
        {
            StopCoroutine(readingCoroutine);
            readingCoroutine = null;
        }
        thermometerText.text = "---";
        UpdateScreenColor(idleColor);
    }
    
    private void SetColorFromTemp(float temp)
    {
        if (temp >= 38f)
        {
            UpdateScreenColor(highColor);
        }
        else if (temp >= 35f)
        {
            UpdateScreenColor(normalColor);
        }
        else
        {
            UpdateScreenColor(idleColor);
        }
    }
    
    private void UpdateScreenColor(Color color)
    {
        if (screenRenderer != null)
        {
            screenRenderer.material.color = color;
        }
    }

    void ShutOff()
    {
        thermometerText.text = "off";
        UpdateScreenColor(idleColor);
        if (readingCoroutine != null) StopCoroutine(readingCoroutine);
    }

    private IEnumerator PerformReading()
    {
        while (isUsing)
        {
            internalBattery.DrainBattery();
            if (internalBattery.IsBatteryEmpty())
            {
                ShutOff();
                yield break;
            }
            
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                SuspectCharacter suspect = hit.collider.GetComponentInParent<SuspectCharacter>();
                if (suspect != null)
                {
                    // Resolve temperature target from the anomaly component if present and active.
                    HighTemperatureAnomaly tempAnomaly = suspect.GetComponentInChildren<HighTemperatureAnomaly>();
                    bool hasAnomaly = tempAnomaly != null && tempAnomaly.IsActive;
                    float targetTemp  = (hasAnomaly ? tempAnomaly.ElevatedTemperature : NormalBaseTemp) + GetRoomTemperatureOffset();
                    float jitterRange = hasAnomaly ? tempAnomaly.JitterRange : NormalJitterRange;

                    // Initial Ramp up
                    float startTemp = 0f;
                    float elapsed = 0f;
                    float duration = 1.0f;

                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime;
                        currentReading = Mathf.Lerp(startTemp, targetTemp, elapsed / duration);
                        thermometerText.text = Mathf.RoundToInt(currentReading).ToString() + "°";
                        yield return null;
                    }

                    // Continuous jittery updates while still hitting the suspect
                    while (isUsing)
                    {
                        // Check if we are still hitting a suspect
                        Ray continuousRay = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
                        if (Physics.Raycast(continuousRay, out RaycastHit continuousHit, maxDistance) && 
                            continuousHit.collider.GetComponentInParent<SuspectCharacter>() != null)
                        {
                            float jitter = UnityEngine.Random.Range(-jitterRange, jitterRange);
                            currentReading = targetTemp + jitter;
                            thermometerText.text = Mathf.RoundToInt(currentReading).ToString() + "°";
                            SetColorFromTemp(currentReading);
                        }
                        else
                        {
                            thermometerText.text = "ERR";
                            UpdateScreenColor(highColor);
                            break; 
                        }
                        yield return new WaitForSeconds(1.0f);
                    }
                }
                else
                {
                    thermometerText.text = "---";
                    UpdateScreenColor(idleColor);
                }
            }
            else
            {
                float roomTemp = 22f + GetRoomTemperatureOffset();
                thermometerText.text = Mathf.RoundToInt(roomTemp).ToString() + "°";
                SetColorFromTemp(roomTemp);
            }
            yield return new WaitForSeconds(0.1f);
        }
    }

    private float GetRoomTemperatureOffset()
    {
        return roomService?.TemperatureOffset ?? 0f;
    }
}
