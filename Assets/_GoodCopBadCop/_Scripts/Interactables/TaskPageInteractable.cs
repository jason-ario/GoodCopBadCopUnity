using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Interactable placed on the wall task page.
/// Interact to enter a diegetic close-up view of the task list.
/// Requires a <see cref="NetworkObject"/> on this GameObject (Interactable is a NetworkBehaviour).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TaskPageInteractable : Interactable, IHeldItemPassthrough
{
    [Tooltip("The diegetic view controller that opens the close-up camera view.")]
    [SerializeField] private TaskPageDiegeticController _diegeticController;

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        _diegeticController?.Open(player);
    }
}
