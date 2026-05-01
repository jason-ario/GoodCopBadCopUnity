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

    private bool canInteract = false;
    private bool isTooFar = false;

    [SerializeField] private TextMeshProUGUI doText; 
    [SerializeField] private Image line;

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
            doText.gameObject.SetActive(false);
        }
        else
        {
            doText.gameObject.SetActive(false);
        }

        // Smoothly change color
        reticle.color = Color.Lerp(reticle.color, targetColor, Time.deltaTime * lerpSpeed); 
        doText.color = Color.Lerp(doText.color, targetColor, Time.deltaTime * lerpSpeed);
        line.color = Color.Lerp(line.color, targetColor, Time.deltaTime * lerpSpeed);
        
        // Smoothly change size
        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * targetScale,
            Time.deltaTime * lerpSpeed
        );
    }

    /// <summary>
    /// Updates the interact reticle state and label.
    /// </summary>
    /// <param name="state">Whether the reticle is in interact mode.</param>
    /// <param name="text">Label to display next to the input hint.</param>
    /// <param name="useKeyPrompt">When true, shows the E-key prompt instead of the mouse-click sprite.</param>
    public void SetInteractState(bool state, string text = "", bool useKeyPrompt = false)
    {
        canInteract = state;
        if (state) isTooFar = false;
        
        if (state == true && text != "")
        {
            doText.gameObject.SetActive(true);
            doText.text = useKeyPrompt ? $"[E] {text}" : $"{text} <sprite=0>";
        }
        else
        {
            doText.gameObject.SetActive(false);
        }
    }

    public void DisableReticle()
    {
        reticle.enabled = false;
        doText.gameObject.SetActive(false);
    }
    
    public void EnableReticle()
    {
        reticle.enabled = true;
    }

    public void SetTooFarState(bool state)
    {
        isTooFar = state;
        // If we are setting too far to true, make sure canInteract is false
        if (state) canInteract = false;
    }
}