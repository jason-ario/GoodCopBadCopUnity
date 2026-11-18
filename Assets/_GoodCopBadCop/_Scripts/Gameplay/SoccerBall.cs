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

    [Header("Weapon Hits (guns / melee)")]
    [Tooltip("Impulse applied by a weapon hit at strength 1 (about one pistol shot). Clamped by maxKickForce.")]
    [SerializeField] private float weaponHitForce = 5f;
    [Tooltip("Upward component added to the weapon hit direction before normalizing, so the ball hops.")]
    [SerializeField] private float weaponHitUpward = 0.25f;

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

    /// <summary>
    /// Called on the attacking player's machine when a gun or melee hit lands on the ball. Pushes it
    /// along the hit direction (with a small hop) through the normal server kick path.
    /// </summary>
    public void RequestWeaponHit(Vector3 direction, float strength)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        Vector3 dir = (direction.normalized + Vector3.up * weaponHitUpward).normalized;
        RequestKick(dir * (weaponHitForce * Mathf.Max(0f, strength)));
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
