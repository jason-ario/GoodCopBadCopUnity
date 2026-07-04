using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ReticleController : MonoBehaviour
{
    public Image reticle;
    public Color normalColor = Color.white;
    public Color interactColor = Color.green;
    public Color tooFarColor = Color.red;

    public float normalScale = 1f;
    public float interactScale = 1.3f;
    public float lerpSpeed = 10f;

    [Header("Extract Hint")]
    [Tooltip("The TMP label that shows the extraction action text (child 'Do Text').")]
    [SerializeField] private TextMeshProUGUI _hintLabel;

    [Tooltip("The GameObject wrapping the key icon (child 'Button Tooltip').")]
    [SerializeField] private GameObject _hintKeyIcon;

    private bool canInteract = false;
    private bool isTooFar = false;

    void Update()
    {
        Color targetColor = normalColor;
        float targetScale = normalScale;

        if (canInteract)
        {
            targetColor = interactColor;
            targetScale = interactScale;
        }
        else if (isTooFar)
        {
            targetColor = tooFarColor;
            targetScale = interactScale;
        }

        reticle.color = Color.Lerp(reticle.color, targetColor, Time.deltaTime * lerpSpeed);

        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * targetScale,
            Time.deltaTime * lerpSpeed
        );
    }

    /// <summary>
    /// Updates the interact reticle state. When <paramref name="showHint"/> is true,
    /// the extract hint (key icon + action text) is shown using <paramref name="text"/>.
    /// All other text/tooltip parameters are kept for call-site compatibility but are not rendered.
    /// </summary>
    /// <param name="state">Whether the reticle is in interact mode.</param>
    /// <param name="text">Action label shown in the extract hint when <paramref name="showHint"/> is true.</param>
    /// <param name="useKeyPrompt">Unused.</param>
    /// <param name="showButtonTooltip">Unused.</param>
    /// <param name="showHint">When true, shows the extract hint elements next to the reticle.</param>
    public void SetInteractState(bool state, string text = "", bool useKeyPrompt = false, bool showButtonTooltip = true, bool showHint = false)
    {
        canInteract = state;
        if (state) isTooFar = false;
        SetHintVisible(showHint, text);
    }

    /// <summary>Hides the reticle entirely.</summary>
    public void DisableReticle()
    {
        reticle.enabled = false;
        SetHintVisible(false);
    }

    /// <summary>Shows the reticle.</summary>
    public void EnableReticle()
    {
        reticle.enabled = true;
    }

    /// <summary>
    /// Marks the reticle as "target too far away", switching it to the too-far color.
    /// </summary>
    public void SetTooFarState(bool state)
    {
        isTooFar = state;
        if (state)
        {
            canInteract = false;
            SetHintVisible(false);
        }
    }

    // ─── Private ─────────────────────────────────────────────────────────────

    private void SetHintVisible(bool visible, string text = "")
    {
        if (_hintLabel != null)
        {
            _hintLabel.gameObject.SetActive(visible);
            if (visible)
                _hintLabel.text = text;
        }

        if (_hintKeyIcon != null)
            _hintKeyIcon.SetActive(visible);
    }
}
