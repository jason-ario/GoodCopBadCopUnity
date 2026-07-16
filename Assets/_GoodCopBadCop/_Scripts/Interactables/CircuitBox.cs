using Unity.Netcode;
using UnityEngine;

public class CircuitBox : Interactable
{
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip circuitBoxOpenSound;
    [SerializeField] private AudioClip circuitBoxCloseSound;
    [SerializeField] private ElectricityController electricityController;

    private bool _isOpened = false;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        ToggleCircuitBoxServerRpc();
    }

    /// <summary>
    /// Requests a circuit box toggle from any client. The server validates and
    /// broadcasts the result so all clients stay in sync.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void ToggleCircuitBoxServerRpc()
    {
        // Block normal restore if this outage requires the fuse-box puzzle.
        if (!electricityController.IsPowerOn && !electricityController.RequiresFuseBoxRestore)
        {
            electricityController.PowerOn();
        }

        _isOpened = !_isOpened;
        UpdateCircuitBoxClientRpc(_isOpened);
    }

    [ClientRpc]
    private void UpdateCircuitBoxClientRpc(bool opened)
    {
        _isOpened = opened;

        if (_isOpened)
        {
            audioSource.PlayOneShot(circuitBoxOpenSound);
            animator.SetBool("Open", true);
        }
        else
        {
            audioSource.PlayOneShot(circuitBoxCloseSound);
            animator.SetBool("Open", false);
        }
    }
}
