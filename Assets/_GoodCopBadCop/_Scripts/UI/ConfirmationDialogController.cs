using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConfirmationDialogController : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private TextMeshProUGUI confirmButtonText;
    [SerializeField] private TextMeshProUGUI cancelButtonText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private Action onConfirm;
    private Action onCancel;

    private void Awake()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(Confirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(Cancel);
        }
    }

    private void OnDestroy()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(Confirm);
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.RemoveListener(Cancel);
        }
    }

    public void Show(
        string title,
        string body,
        string confirmText,
        string cancelText,
        Action confirmCallback,
        Action cancelCallback = null)
    {
        onConfirm = confirmCallback;
        onCancel = cancelCallback;

        SetText(titleText, title);
        SetText(bodyText, body);
        SetText(confirmButtonText, confirmText);
        SetText(cancelButtonText, cancelText);

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        onConfirm = null;
        onCancel = null;
        gameObject.SetActive(false);
    }

    private void Confirm()
    {
        Action callback = onConfirm;
        Hide();
        callback?.Invoke();
    }

    private void Cancel()
    {
        Action callback = onCancel;
        Hide();
        callback?.Invoke();
    }

    private static void SetText(TextMeshProUGUI target, string value)
    {
        if (target != null)
        {
            target.text = value;
        }
    }
}
