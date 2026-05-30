using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ToiletController : Interactable
{
    private const float FlushAnimDuration = 0.5f;

    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _flushSound;
    [SerializeField] private Animator _animator;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        // Play locally with no round-trip delay, then propagate to others.
        PlayFlush();
        FlushServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    /// <summary>
    /// Plays the flush sound and pulses the "Flush" animator bool for FlushAnimDuration seconds.
    /// </summary>
    private void PlayFlush()
    {
        _audioSource.PlayOneShot(_flushSound);
        StartCoroutine(PulseFlushBool());
    }

    private IEnumerator PulseFlushBool()
    {
        _animator.SetBool("Flush", true);
        yield return new WaitForSeconds(FlushAnimDuration);
        _animator.SetBool("Flush", false);
    }

    [ServerRpc(RequireOwnership = false)]
    private void FlushServerRpc(ulong senderClientId)
    {
        FlushClientRpc(senderClientId);
    }

    /// <summary>
    /// Replicates the flush effect on all clients except the one that triggered it locally.
    /// </summary>
    [ClientRpc]
    private void FlushClientRpc(ulong senderClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == senderClientId) return;
        PlayFlush();
    }
}
