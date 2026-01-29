using UnityEngine;

public class CharactersChillingCutscene : MonoBehaviour
{
    [SerializeField] private ParticleSystem smokeBreath;

    public void SmokeOn()
    {
        smokeBreath.Play();
    }
    
    public void SmokeOff()
    {
        smokeBreath.Stop();
    }
}
