using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Subscribes to <see cref="GlobalHostVariables.money"/> and keeps the assigned
/// <see cref="TextMeshProUGUI"/> in sync with the current coupon amount.
/// Assign the target label via <see cref="SetLabel"/> or the Inspector field.
/// </summary>
public class CouponText : MonoBehaviour
{
    [SerializeField] private TextMeshPro _label;

    /// <summary>Assigns the TMP label at runtime instead of through the Inspector.</summary>
    public void SetLabel(TextMeshPro label)
    {
        _label = label;

        if (GlobalHostVariables.Instance != null)
        {
            UpdateText(GlobalHostVariables.Instance.money.Value, GlobalHostVariables.Instance.money.Value);
        }
    }

    private IEnumerator Start()
    {
        while (GlobalHostVariables.Instance == null)
        {
            yield return new WaitForEndOfFrame();
        }

        GlobalHostVariables.Instance.money.OnValueChanged += UpdateText;
        UpdateText(GlobalHostVariables.Instance.money.Value, GlobalHostVariables.Instance.money.Value);
    }

    private void OnEnable()
    {
        if (GlobalHostVariables.Instance != null)
        {
            GlobalHostVariables.Instance.money.OnValueChanged += UpdateText;
        }
    }

    private void OnDisable()
    {
        if (GlobalHostVariables.Instance != null)
        {
            GlobalHostVariables.Instance.money.OnValueChanged -= UpdateText;
        }
    }

    private void UpdateText(int previousValue, int newValue)
    {
        if (_label != null)
        {
            _label.text = newValue.ToString();
        }
    }
}
