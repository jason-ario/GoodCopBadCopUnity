using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Scrolls the payment amount across the ATM screen render texture using a
/// classic digital-display marquee: text enters from the right edge and exits
/// off the left edge, one character step at a time.
///
/// Each step: update TMP text → camera snapshot (one frame) → wait 1 / scrollFps seconds.
/// At the default 0.5 fps that is one step every 2 seconds, giving a slow,
/// chunky old-fashioned segmented-display feel.
///
/// Scene setup (unchanged from previous version):
///   - Camera.targetTexture           → ATM Screen.renderTexture  (set in Inspector)
///   - ATM Text Screen.mat._OverlayMap → ATM Screen.renderTexture  (set in material)
///   - Assign _amountText             → ATM Screen Contents/Amount Text (TextMeshPro)
///   - Assign _camera                 → ATM Screen Contents/Camera  (disabled by default)
/// </summary>
public class ATMScreenController : MonoBehaviour
{
    [Header("Content")]
    [Tooltip("World-space TextMeshPro on the HiddenUI layer that displays the payment amount.")]
    [SerializeField] private TextMeshPro _amountText;

    [Header("Render Texture")]
    [Tooltip("Camera on the HiddenUI layer. targetTexture must be ATM Screen.renderTexture. Disabled by default.")]
    [SerializeField] private GameObject _camera;

    [Header("Scroll Animation")]
    [Tooltip("How many characters the display shows at once.  Match this to how many glyphs visually fit on the ATM screen.")]
    [SerializeField] private int _displayWidth = 10;

    [Tooltip("Steps per second for the marquee tick.  0.5 = one character advance every 2 seconds.")]
    [SerializeField] private float _scrollFps = 0.5f;

    [Tooltip("Extra blank characters appended after the message so it fully exits the display before clearing.")]
    [SerializeField] private int _trailingPad = 3;

    // Legacy field kept so existing scene wiring that used _displayDuration is not lost on re-serialise.
    // It is no longer used at runtime; the scroll animation defines its own duration.
    [HideInInspector]
    [SerializeField] private float _displayDuration = 3f;

    private Coroutine _scrollRoutine;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the marquee animation for <paramref name="amount"/> dollars.
    /// Safe to call while a previous animation is running — cancels it immediately.
    /// </summary>
    public void ShowPayment(int amount)
    {
        if (_amountText == null || _camera == null) return;

        if (_scrollRoutine != null)
            StopCoroutine(_scrollRoutine);

        _scrollRoutine = StartCoroutine(ScrollRoutine(amount));
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    private IEnumerator ScrollRoutine(int amount)
    {
        // Build the full message that scrolls across the display.
        string message = $"{amount} COUPONS";

        // Pad the left with displayWidth spaces so the text starts fully off-screen right.
        // Pad the right with trailingPad spaces so it exits cleanly before we blank the display.
        string padded = new string(' ', _displayWidth) + message + new string(' ', _trailingPad);

        float stepDelay = _scrollFps > 0f ? 1f / _scrollFps : 2f;
        int windowEnd = padded.Length - _displayWidth;   // last valid window start index

        for (int i = 0; i <= windowEnd; i++)
        {
            _amountText.text = padded.Substring(i, _displayWidth);
            yield return StartCoroutine(CameraSnapshot());
            yield return new WaitForSeconds(stepDelay);
        }

        // Clear the display and take a final snapshot to blank the render texture.
        _amountText.text = string.Empty;
        yield return StartCoroutine(CameraSnapshot());

        _scrollRoutine = null;
    }

    // ── Render-texture helpers ────────────────────────────────────────────────

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has
    /// been submitted.  Identical pattern to DailyFaxContentsController.
    /// </summary>
    private IEnumerator CameraSnapshot()
    {
        yield return new WaitForEndOfFrame();
        _camera.SetActive(true);
        yield return new WaitForEndOfFrame();
        _camera.SetActive(false);
    }
}
