using UnityEngine;

public class MachineGunController : MonoBehaviour
{
    [SerializeField] ParticleSystem smoke;

    public void SmokeOn()
    {
        smoke.Play();
    }

    public void SmokeOff()
    {
        smoke.Stop();
    }
}
