using UnityEngine;

/// <summary>
/// Marker component placed on a child collider GameObject to point back to the
/// owning <see cref="Interactable"/> or <see cref="ShopItem"/> on a parent GameObject.
/// <see cref="PlayerInteractionController"/> resolves <see cref="Interactable"/> from this
/// after a raycast hit; <see cref="DiegeticViewController"/> subclasses resolve
/// <see cref="ShopItem"/> the same way.
/// </summary>
public class InteractableCollider : MonoBehaviour
{
    public Interactable Interactable;
    public ShopItem ShopItem;
}
