using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Flashes the payment amount on the ATM screen render texture.
///
/// Follows the same stateless snapshot pattern as DailyFaxContentsController:
/// no render texture or material instances are created at runtime. The camera's
/// targetTexture and the screen mesh material are pre-wired directly to the
/// ATM Screen.renderTexture asset in the scene, so the only runtime work is
/// updating the TMP text and toggling the camera for one frame.
///
/// Scene setup:
///   - Camera.targetTexture           → ATM Screen.renderTexture  (set in Inspector)
///   - ATM Text Screen.mat._OverlayMap → ATM Screen.renderTexture  (set in material)
///   - Assign _amountText             → ATM Screen Contents/Amount Text (TextMeshPro)
///   - Assign _camera                 → ATM Screen Contents/Camera  (disabled by default)
///   - Tune  _displayDuration         → seconds the amount stays visible (default 3)
/// </summary>
public class ATMScreenController : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("World-space TextMeshPro on the HiddenUI layer that displays the payment amount.")]
    [SerializeField] private TextMeshPro _amountText;

    [Header("Render Texture")]
    [Tooltip("Camera on the HiddenUI layer. targetTexture must be ATM Screen.renderTexture. Disabled by default.")]
    [SerializeField] private GameObject _camera;

    [Header("Timing")]
    [Tooltip("How many seconds the payment amount stays visible before the screen clears.")]
    [SerializeField] private float _displayDuration = 3f;

    private Coroutine _hideRoutine;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Flashes the payment amount on the ATM screen for <see cref="_displayDuration"/> seconds.
    /// Safe to call while a previous flash is still showing — cancels the previous hide timer.
    /// </summary>
    public void ShowPayment(int amount)
    {
        if (_amountText == null || _camera == null) return;

        _amountText.text = $"PAYMENT\n${amount}";

        if (_hideRoutine != null)
            StopCoroutine(_hideRoutine);

        _hideRoutine = StartCoroutine(ShowAndHide());
    }

    // ── Snapshot & timing ─────────────────────────────────────────────────────

    private IEnumerator ShowAndHide()
    {
        yield return StartCoroutine(CameraSnapshot());
        yield return new WaitForSeconds(_displayDuration);

        _amountText.text = string.Empty;
        yield return StartCoroutine(CameraSnapshot());

        _hideRoutine = null;
    }

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has been submitted.
    /// Identical to DailyFaxContentsController.CameraSnapshot().
    /// </summary>
    private IEnumerator CameraSnapshot()
    {
        yield return new WaitForEndOfFrame();
        _camera.SetActive(true);
        yield return new WaitForEndOfFrame();
        _camera.SetActive(false);
    }
}
