using UnityEngine;

/// <summary>
/// Minimal helper that exposes <see cref="GameObject.SetActive"/> as methods callable from a
/// persistent <see cref="UnityEngine.Events.UnityEvent"/> in the Inspector (e.g. wired to
/// <c>WorldPurchaseActionInteractable</c>'s "On Purchase Confirmed" event to permanently reveal
/// a purchased world object such as the booth PC, Radio, or TV).
///
/// Unity's persistent UnityEvent calls require a Component target, so a raw GameObject reference
/// cannot be wired directly — attach this component to the object you want to activate/deactivate
/// and wire the event to <see cref="Activate"/> or <see cref="Deactivate"/> instead.
/// </summary>
public class GameObjectActivator : MonoBehaviour
{
    [Tooltip("The GameObject to activate/deactivate. Defaults to this GameObject when left empty.")]
    [SerializeField] private GameObject _target;

    public void Activate() => SetActive(true);

    public void Deactivate() => SetActive(false);

    public void SetActive(bool active)
    {
        GameObject target = _target != null ? _target : gameObject;
        target.SetActive(active);
    }
}
