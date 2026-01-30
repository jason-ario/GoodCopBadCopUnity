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
            Debug.Log("Too far");
            targetColor = tooFarColor;
            targetScale = interactScale; // Keep it slightly larger when hovering too far
        }

        // Smoothly change color
        reticle.color = Color.Lerp(reticle.color, targetColor, Time.deltaTime * lerpSpeed);

        // Smoothly change size
        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * targetScale,
            Time.deltaTime * lerpSpeed
        );
    }

    public void SetInteractState(bool state)
    {
        canInteract = state;
        if (state) isTooFar = false;
    }

    public void SetTooFarState(bool state)
    {
        isTooFar = state;
        // If we are setting too far to true, make sure canInteract is false
        if (state) canInteract = false;
    }
}