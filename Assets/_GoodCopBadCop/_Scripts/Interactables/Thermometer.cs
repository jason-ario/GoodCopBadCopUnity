using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class Thermometer : PickableObject
{
    private bool isUsing;
    [SerializeField] TextMeshPro thermometerText;
    [SerializeField] private float maxDistance = 3f;
    
    private Coroutine readingCoroutine;
    private float currentReading;
    private const float TargetBaseTemp = 45.5f;
    
    [SerializeField] private MeshRenderer screenRenderer;
    [SerializeField] private Color highColor;
    [SerializeField] private Color normalColor;
    [SerializeField] private Color idleColor;
    public override void OnStartUse()
    {
        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingThermometerGun", true);
        
        if (readingCoroutine != null) StopCoroutine(readingCoroutine);
        readingCoroutine = StartCoroutine(PerformReading());
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
        playerPickupController.PlayerAnimationController.SetAnimBool("UsingThermometerGun", false);
        
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



    private IEnumerator PerformReading()
    {
        while (isUsing)
        {
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                SuspectCharacter suspect = hit.collider.GetComponentInParent<SuspectCharacter>();
                if (suspect != null)
                {
                    // Initial Ramp up
                    float startTemp = 0f;
                    float elapsed = 0f;
                    float duration = 1.0f;

                    while (elapsed < duration)
                    {
                        elapsed += Time.deltaTime;
                        currentReading = Mathf.Lerp(startTemp, TargetBaseTemp, elapsed / duration);
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
                            float jitter = UnityEngine.Random.Range(-1f, 1f);
                            currentReading = TargetBaseTemp + jitter;
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
                thermometerText.text = "0°";
            }
            yield return new WaitForSeconds(0.1f);
        }
    }
}
