using UnityEngine;

public class TextAnimatorCompletionRelay : MonoBehaviour
{
    public bool IsComplete { get; private set; }

    public void ResetState()
    {
        IsComplete = false;
    }

    // Hook this up from the TypewriterComponent's OnTextShowed UnityEvent
    public void HandleTextShowed()
    {
        IsComplete = true;
    }
}