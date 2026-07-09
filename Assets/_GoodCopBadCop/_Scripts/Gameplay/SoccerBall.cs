using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(NetworkObject))]
public class SoccerBall : NetworkBehaviour
{
    [SerializeField] private float maxKickForce = 12f;
    [SerializeField] private AudioClip kickSound;
    [SerializeField] [Range(0f, 1f)] private float kickVolume = 1f;

    private Rigidbody _rb;
    private AudioSource _audio;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _audio = GetComponent<AudioSource>();
    }

    /// <summary>
    /// Called by a client-owned player to request a kick on the server.
    /// </summary>
    public void RequestKick(Vector3 force)
    {
        KickServerRpc(force);
    }

    [Rpc(SendTo.Server)]
    private void KickServerRpc(Vector3 force)
    {
        Vector3 clamped = Vector3.ClampMagnitude(force, maxKickForce);
        _rb.AddForce(clamped, ForceMode.Impulse);
        PlayKickSoundClientRpc();
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayKickSoundClientRpc()
    {
        if (kickSound != null)
            _audio.PlayOneShot(kickSound, kickVolume);
    }
}
