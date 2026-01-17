using UnityEngine;

public class GateController : MonoBehaviour
{
    [SerializeField] Animator gateAnimator;
    
    public void OpenGate()
    {
        gateAnimator.SetBool("Open", true);
    }
    
    public void CloseGate()
    {
        gateAnimator.SetBool("Open", false);
    }
}
