using Unity.Netcode;
using UnityEngine;

public class HQOrderScreen : MonoBehaviour
{
    [SerializeField] private Telephone _telephone;
    [SerializeField] private AudioSource _loopingAudio;

    private void OnEnable()
    {
        if (_loopingAudio != null)
        {
            _loopingAudio.Play();
        }
    }

    private void OnDisable()
    {
        if (_loopingAudio != null)
        {
            _loopingAudio.Stop();
        }
    }

    /// <summary>
    /// Hangs up the telephone, triggering the put-down sequence and closing this screen.
    /// Intended to be called by the back button's onClick event.
    /// </summary>
    public void HangUp()
    {
        _telephone.HangUp(NetworkManager.Singleton.LocalClientId);
    }
}
