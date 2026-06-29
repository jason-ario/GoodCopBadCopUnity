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
    /// Updates the interact reticle state. Text and tooltip parameters are kept for
    /// call-site compatibility but are no longer rendered.
    /// </summary>
    /// <param name="state">Whether the reticle is in interact mode.</param>
    /// <param name="text">Unused — interact text has been removed from the reticle.</param>
    /// <param name="useKeyPrompt">Unused — button tooltip has been removed from the reticle.</param>
    /// <param name="showButtonTooltip">Unused — button tooltip has been removed from the reticle.</param>
    public void SetInteractState(bool state, string text = "", bool useKeyPrompt = false, bool showButtonTooltip = true)
    {
        canInteract = state;
        if (state) isTooFar = false;
    }

    /// <summary>Hides the reticle entirely.</summary>
    public void DisableReticle()
    {
        reticle.enabled = false;
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
        if (state) canInteract = false;
    }
}
