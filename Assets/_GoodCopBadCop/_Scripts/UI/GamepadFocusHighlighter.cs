using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Visible focus for plain UI Buttons selected through the EventSystem (gamepad / keyboard navigation).
/// Many buttons only have a ColorTint whose Selected colour is almost identical to Normal, so a focused
/// button looked unfocused. This scales the currently selected Button up slightly while it is selected.
///
/// Buttons that already provide their own focus visuals are left alone: any button with an Animator,
/// an Animation transition, another component handling select/pointer-enter (TextButton, PanelButton...),
/// or living inside a <see cref="CampaignSlot"/> (which scales its own buttons).
/// Created automatically once per session; no scene setup required.
/// </summary>
public class GamepadFocusHighlighter : MonoBehaviour
{
    private const float FocusScale = 1.08f;
    private const float GrowDuration = 0.08f;

    private static GamepadFocusHighlighter _instance;

    private readonly List<MonoBehaviour> _behaviourBuffer = new List<MonoBehaviour>();
    private Transform _target;
    private Vector3 _baseScale;
    private float _progress;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        GameObject go = new GameObject("[GamepadFocusHighlighter]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<GamepadFocusHighlighter>();
    }

    private void LateUpdate()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
        Transform wanted = IsEligible(selected) ? selected.transform : null;

        if (wanted != _target)
        {
            Restore();
            if (wanted != null)
            {
                _target = wanted;
                _baseScale = wanted.localScale;
                _progress = 0f;
            }
        }

        if (_target == null) return;

        _progress = Mathf.MoveTowards(_progress, 1f, Time.unscaledDeltaTime / GrowDuration);
        _target.localScale = _baseScale * Mathf.Lerp(1f, FocusScale, Mathf.SmoothStep(0f, 1f, _progress));
    }

    private void OnDisable() => Restore();

    private void Restore()
    {
        if (_target != null) _target.localScale = _baseScale;
        _target = null;
    }

    private bool IsEligible(GameObject go)
    {
        if (go == null || !go.activeInHierarchy) return false;

        Button button = go.GetComponent<Button>();
        if (button == null || !button.IsInteractable()) return false;
        if (button.transition == Selectable.Transition.Animation) return false;
        if (go.GetComponent<Animator>() != null) return false;
        if (go.GetComponentInParent<CampaignSlot>() != null) return false;

        go.GetComponents(_behaviourBuffer);
        foreach (MonoBehaviour behaviour in _behaviourBuffer)
        {
            if (behaviour == null || behaviour is Selectable) continue;
            if (behaviour is ISelectHandler || behaviour is IPointerEnterHandler) return false;
        }
        return true;
    }
}
