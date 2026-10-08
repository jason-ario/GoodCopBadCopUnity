using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a faded <see cref="CanvasGroup"/> from taking pointer or navigation input until it is
/// visible enough to see. Animator-driven fades (e.g. <c>Canvas Open</c> / <c>Canvas Open Slow</c>)
/// only animate alpha, so without this the buttons can be hovered and clicked while they are still
/// invisible or faint.
///
/// Every LateUpdate (after Animators have written alpha) this sets the group's
/// <see cref="CanvasGroup.interactable"/> and <see cref="CanvasGroup.blocksRaycasts"/> from its
/// <em>effective</em> alpha, which is its own alpha multiplied by every enabled parent CanvasGroup
/// up to (and including) the first one with <see cref="CanvasGroup.ignoreParentGroups"/>. A parent
/// fade, such as the splash screen fading the whole main menu in, also counts.
///
/// This component takes over both flags on its own group. Disable input from a parent group or
/// another group instead of writing them here. The gate closes on enable, so a re-shown screen
/// whose fade restarts from 0 can't be hovered for a frame at its old alpha.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
[DisallowMultipleComponent]
public class CanvasGroupFadeInputGate : MonoBehaviour
{
    [Tooltip("Effective alpha (own alpha x parent CanvasGroup alphas) at which the group starts accepting input.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float interactableAlpha = 0.8f;

    private CanvasGroup _group;
    private readonly List<CanvasGroup> _parentGroups = new List<CanvasGroup>();

    private void Awake()
    {
        _group = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        CacheParentGroups();
        SetOpen(false);
    }

    private void OnTransformParentChanged()
    {
        CacheParentGroups();
    }

    private void LateUpdate()
    {
        SetOpen(EffectiveAlpha() >= interactableAlpha);
    }

    private void CacheParentGroups()
    {
        _parentGroups.Clear();
        if (_group == null || _group.ignoreParentGroups)
            return;

        Transform parent = transform.parent;
        if (parent == null)
            return;

        // Nearest parent first, so the walk can stop at the first group that ignores its parents.
        parent.GetComponentsInParent(true, _parentGroups);
    }

    private float EffectiveAlpha()
    {
        float alpha = _group.alpha;
        if (_group.ignoreParentGroups)
            return alpha;

        for (int i = 0; i < _parentGroups.Count; i++)
        {
            CanvasGroup parent = _parentGroups[i];
            if (parent == null || !parent.enabled)
                continue;

            alpha *= parent.alpha;
            if (parent.ignoreParentGroups)
                break;
        }

        return alpha;
    }

    private void SetOpen(bool open)
    {
        if (_group == null)
            return;

        if (_group.interactable != open)
            _group.interactable = open;
        if (_group.blocksRaycasts != open)
            _group.blocksRaycasts = open;
    }
}
