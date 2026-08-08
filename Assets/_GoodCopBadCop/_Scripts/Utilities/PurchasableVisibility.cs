using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps a purchasable network object (e.g. the booth PC or Radio) permanently active/spawned so
/// its <see cref="NetworkObject"/> synchronizes correctly for every client, while disabling the
/// specific renderer/collider/script components that make it visible and usable until the item is
/// purchased.
///
/// Unlike <see cref="GameObjectActivator"/> (which toggles the GameObject it lives on), this
/// component's own GameObject — and the GameObjects of everything referenced below — must never be
/// deactivated. Deactivating a scene-placed NetworkObject before it spawns permanently prevents
/// Netcode from ever spawning it. Instead, this only flips the `enabled` flag on individual
/// components, which is safe to do at any time regardless of NetworkObject spawn state.
///
/// Wire a purchase-confirmed event (e.g. from <see cref="WorldPurchaseActionInteractable"/>) to
/// <see cref="Unlock"/>.
///
/// Purchase state is stored in a <see cref="NetworkVariable{T}"/> instead of only reacting to a
/// one-shot event, so clients that join the session after the purchase already happened still
/// receive the correct unlocked state when this object spawns for them.
/// </summary>
public class PurchasableVisibility : NetworkBehaviour
{
    [Header("Locked Until Purchase")]
    [Tooltip("Renderers to enable only after purchase (the visible mesh of the object).")]
    [SerializeField] private Renderer[] _renderersToUnlock;

    [Tooltip("Colliders to enable only after purchase (interaction/highlight trigger volumes).")]
    [SerializeField] private Collider[] _collidersToUnlock;

    [Tooltip("Other components (e.g. the interactable script, AudioSource) to enable only after purchase.")]
    [SerializeField] private Behaviour[] _behavioursToUnlock;

    private readonly NetworkVariable<bool> _isUnlocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _isUnlocked.OnValueChanged += OnUnlockedChanged;
        Apply(_isUnlocked.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isUnlocked.OnValueChanged -= OnUnlockedChanged;
        base.OnNetworkDespawn();
    }

    private void OnUnlockedChanged(bool previousValue, bool newValue) => Apply(newValue);

    private void Apply(bool unlocked)
    {
        foreach (Renderer r in _renderersToUnlock)
            if (r != null) r.enabled = unlocked;

        foreach (Collider c in _collidersToUnlock)
            if (c != null) c.enabled = unlocked;

        foreach (Behaviour b in _behavioursToUnlock)
            if (b != null) b.enabled = unlocked;
    }

    /// <summary>
    /// Marks this object as purchased/unlocked. Safe to call from any peer — only the server
    /// actually writes the NetworkVariable, and the change then replicates to everyone (including
    /// clients that join later). Wire this to a purchase stand's "On Purchase Confirmed" event.
    /// </summary>
    public void Unlock()
    {
        if (IsServer)
            _isUnlocked.Value = true;
    }
}
