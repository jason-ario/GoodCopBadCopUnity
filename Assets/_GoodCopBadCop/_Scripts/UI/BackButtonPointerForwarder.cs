using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Forwards pointer clicks from the visible child label of the shared Back button
/// prefab to its parent Back button, which owns the current screen's action.
/// </summary>
[RequireComponent(typeof(Button))]
public class BackButtonPointerForwarder : MonoBehaviour, IPointerClickHandler
{
    private Button _button;
    private KeyBackButtonActivator _backButtonActivator;

    private void Awake()
    {
        _button = GetComponent<Button>();
        _backButtonActivator = GetComponentInParent<KeyBackButtonActivator>(true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || _button == null
            || !_button.isActiveAndEnabled
            || !_button.interactable)
            return;

        _backButtonActivator?.InvokeButton();
    }
}
