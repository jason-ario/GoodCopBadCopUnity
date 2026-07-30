using DG.Tweening;
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
///
/// Optionally plays a DOTween DOPunchScale "pop" on the target's transform when activating.
/// </summary>
public class GameObjectActivator : MonoBehaviour
{
    [Tooltip("The GameObject to activate/deactivate. Defaults to this GameObject when left empty.")]
    [SerializeField] private GameObject _target;

    [Header("Activate Punch Scale")]
    [Tooltip("If true, plays a DOPunchScale bump on the target's transform whenever it is activated.")]
    [SerializeField] private bool _punchScaleOnActivate = true;
    [SerializeField] private Vector3 _punchStrength = new Vector3(0.2f, 0.2f, 0.2f);
    [SerializeField] private float _punchDuration = 0.35f;
    [SerializeField] private int _punchVibrato = 6;
    [SerializeField][Range(0f, 1f)] private float _punchElasticity = 0.5f;

    public void Activate() => SetActive(true);

    public void Deactivate() => SetActive(false);

    public void SetActive(bool active)
    {
        GameObject target = _target != null ? _target : gameObject;
        bool wasActive = target.activeSelf;
        target.SetActive(active);

        if (active && !wasActive && _punchScaleOnActivate)
            PlayPunchScale(target.transform);
    }

    private void PlayPunchScale(Transform target)
    {
        target.DOKill(complete: true);
        target.DOPunchScale(Vector3.Scale(target.localScale, _punchStrength), _punchDuration, _punchVibrato, _punchElasticity);
    }
}
