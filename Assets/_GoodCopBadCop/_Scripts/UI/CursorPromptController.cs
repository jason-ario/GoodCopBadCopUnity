using TMPro;
using UnityEngine;

/// <summary>
/// A screen-space prompt that follows the cursor with a configurable offset.
/// Designed for cursor-driven interaction contexts (e.g. diegetic views) where
/// the standard reticle system is suppressed.
///
/// Usage:
///   cursorPrompt.Show("Interact");   // shows the label and begins tracking
///   cursorPrompt.Hide();             // hides the label
///
/// The GameObject starts inactive; <see cref="Show"/> activates it,
/// <see cref="Hide"/> deactivates it. Any child objects (e.g. a decorative line)
/// are hidden and shown automatically along with it.
/// </summary>
public class CursorPromptController : MonoBehaviour
{
    [Tooltip("The TMP label that displays the hint text.")]
    [SerializeField] private TextMeshProUGUI _label;

    [Tooltip("Screen-pixel offset from the raw cursor position to the prompt anchor point.")]
    [SerializeField] private Vector2 _cursorOffset = new Vector2(18f, 22f);

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>Whether the prompt is currently visible.</summary>
    public bool IsVisible => gameObject.activeSelf;

    /// <summary>
    /// Shows the prompt with <paramref name="text"/> and starts tracking the cursor.
    /// </summary>
    public void Show(string text)
    {
        _label.text = text;
        gameObject.SetActive(true);
    }

    /// <summary>Hides the prompt and stops cursor tracking.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs only while active. Moves the RectTransform to the cursor position plus
    /// <see cref="_cursorOffset"/>. Works with Screen Space Overlay canvases where
    /// <c>RectTransform.position</c> maps 1:1 to screen pixels.
    /// </summary>
    private void Update()
    {
        transform.position = new Vector3(
            Input.mousePosition.x + _cursorOffset.x,
            Input.mousePosition.y + _cursorOffset.y,
            0f
        );
    }
}
