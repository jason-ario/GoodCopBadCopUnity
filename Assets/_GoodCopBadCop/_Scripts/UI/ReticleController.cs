using UnityEngine;
using UnityEngine.UI;

public class ReticleController : MonoBehaviour
{
    public Image reticle;
    public Color normalColor = Color.white;
    public Color interactColor = Color.yellow;

    public float normalScale = 1f;
    public float interactScale = 1.3f;
    public float lerpSpeed = 10f;

    private bool canInteract = false;

    void Update()
    {
        // Smoothly change color
        reticle.color = Color.Lerp(
            reticle.color,
            canInteract ? interactColor : normalColor,
            Time.deltaTime * lerpSpeed
        );

        // Smoothly change size
        reticle.rectTransform.localScale = Vector3.Lerp(
            reticle.rectTransform.localScale,
            Vector3.one * (canInteract ? interactScale : normalScale),
            Time.deltaTime * lerpSpeed
        );
    }

    public void SetInteractState(bool state)
    {
        canInteract = state;
    }
}