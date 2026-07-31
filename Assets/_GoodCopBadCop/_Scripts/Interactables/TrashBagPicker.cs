using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A stationary, world-space trash bag dispenser with an unlimited supply.
///
/// Unlike <see cref="TrashBagRoll"/> (a <see cref="ContainerPickableObject"/> that holds a
/// finite, carryable roll of bags), this dispenser is never picked up itself — it stays in
/// place permanently and can be used by any player, any number of times.
///
/// Press E or LMB (empty-handed) while targeting the dispenser to receive one
/// <see cref="TrashBag"/> directly into your hands. Both keys perform the exact same action.
///
/// Prefab requirements:
///   - NetworkObject
///   - HighlightEffect  (required by Interactable)
///   - Collider on the Interactable layer
///   - "Dispensed Item Data" field → <c>Trash Bag.asset</c> (PickableItemData given on interact)
/// Must be registered as a Network Prefab in the NetworkManager if it will be spawned at
/// runtime (not required for instances placed directly in a scene).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TrashBagPicker : Interactable
{
    [Header("Dispenser")]
    [Tooltip("PickableItemData for the trash bag given to the player on interact.")]
    [SerializeField] private PickableItemData _dispensedItemData;

    [Tooltip("Optional spawn point for the dispensed bag. Defaults to this object's transform.")]
    [SerializeField] private Transform _spawnPoint;

    [Tooltip("Reticle tooltip shown while this dispenser is targeted.")]
    [SerializeField] private string _interactLabel = "Grab a Trash Bag";

    [Tooltip("Sound played on every successful dispense (optional).")]
    [SerializeField] private AudioClip _dispenseSound;

    /// <summary>
    /// Fired locally, on whichever client successfully grabs a bag from ANY
    /// <see cref="TrashBagPicker"/> instance. Purely a local detection signal — day scripts that
    /// need every connected client to react (e.g. dismissing a tutorial arrow) should relay this
    /// through <see cref="TutorialTaskSync"/> rather than subscribing directly.
    /// </summary>
    public static event Action OnBagDispensedLocally;

    protected override void Awake()
    {
        base.Awake();
        interactText = _interactLabel;
    }

    /// <summary>Always shows the extract hint (key icon + action text) while this dispenser is targeted.</summary>
    public override bool ShowInteractHint => true;

    /// <summary>LMB (empty-handed) grabs a bag — identical to pressing E.</summary>
    public override void Interact(PlayerInteractionController player) => GiveBag(player);

    /// <summary>E grabs a bag — identical to LMB.</summary>
    public override void InteractAlternate(PlayerInteractionController player) => GiveBag(player);

    /// <summary>
    /// Spawns one trash bag and routes it directly into the requesting player's hands.
    /// The supply is unlimited — this dispenser never depletes and never despawns.
    /// </summary>
    private void GiveBag(PlayerInteractionController player)
    {
        if (player.pickupController.HeldObject != null) return;

        if (_dispensedItemData == null || _dispensedItemData.PickUpPrefab == null)
        {
            Debug.LogError($"[{GetType().Name}] _dispensedItemData is not assigned or missing a pickup prefab.");
            return;
        }

        Transform spawn = _spawnPoint != null ? _spawnPoint : transform;
        player.pickupController.SpawnAndPickUp(_dispensedItemData, spawn);

        if (_dispenseSound != null)
            SFXController.Instance.PlayAtPosition(_dispenseSound, transform.position);

        OnBagDispensedLocally?.Invoke();
    }
}
