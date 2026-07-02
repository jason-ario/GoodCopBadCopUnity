using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the bunk bed popup shown when the player interacts with the bunk bed.
/// In the ready state it shows "End the day?" with Yes / No buttons.
/// In the blocked state it shows "Can't sleep yet" with only the Back UI for exit.
/// Configure via <see cref="Setup"/> or <see cref="SetupBlocked"/> before activating the root GameObject.
/// </summary>
public class EndDayPopupUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _titleLabel;
    [SerializeField] private Button _confirmButton;
    [SerializeField] private Button _cancelButton;
    [SerializeField] private GameObject _buttonsContainer;

    private const string TitleReady   = "End the day?";
    private const string TitleBlocked = "Can't sleep yet";

    private Action _onConfirm;
    private Action _onCancel;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_confirmButton != null)
            _confirmButton.onClick.AddListener(OnConfirmClicked);
        if (_cancelButton != null)
            _cancelButton.onClick.AddListener(OnCancelClicked);
    }

    private void OnDestroy()
    {
        if (_confirmButton != null)
            _confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (_cancelButton != null)
            _cancelButton.onClick.RemoveListener(OnCancelClicked);
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Configures the popup for the "End the day?" flow.
    /// Shows the title and Yes / No buttons.
    /// </summary>
    /// <param name="onConfirm">Callback invoked when the player confirms ending the day.</param>
    /// <param name="onCancel">Callback invoked when the player presses the No button.</param>
    public void Setup(Action onConfirm, Action onCancel)
    {
        _onConfirm = onConfirm;
        _onCancel  = onCancel;

        if (_titleLabel != null)
            _titleLabel.text = TitleReady;

        if (_buttonsContainer != null)
            _buttonsContainer.SetActive(true);
    }

    /// <summary>
    /// Configures the popup for the "Can't sleep yet" blocked state.
    /// Hides the Yes / No buttons — the Back UI button is the only exit.
    /// </summary>
    /// <param name="onCancel">Callback invoked when the player dismisses the popup.</param>
    public void SetupBlocked(Action onCancel)
    {
        _onConfirm = null;
        _onCancel  = onCancel;

        if (_titleLabel != null)
            _titleLabel.text = TitleBlocked;

        if (_buttonsContainer != null)
            _buttonsContainer.SetActive(false);
    }

    /// <summary>Called by the Yes button's OnClick event.</summary>
    public void OnConfirmClicked()
    {
        _onConfirm?.Invoke();
    }

    /// <summary>Called by the No button's OnClick event.</summary>
    public void OnCancelClicked()
    {
        _onCancel?.Invoke();
    }
}
