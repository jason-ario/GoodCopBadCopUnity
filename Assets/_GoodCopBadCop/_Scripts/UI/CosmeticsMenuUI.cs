using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI panel for the Cosmetics Locker.
/// Dynamically creates a button for each hat registered in the local player's
/// <see cref="PlayerHatController"/>, plus a "No Hat" button (index -1).
/// Highlights the button for the currently equipped hat and subscribes to
/// <see cref="PlayerHatController.OnHatChanged"/> so the highlight stays in sync
/// even though equipping goes through a server round-trip.
/// </summary>
public class CosmeticsMenuUI : MonoBehaviour
{
    [Header("Button Layout")]
    [Tooltip("Parent transform (e.g. a HorizontalLayoutGroup or GridLayoutGroup) where hat buttons are spawned.")]
    [SerializeField] private Transform _buttonContainer;

    [Tooltip("Template button that is cloned for each hat entry. Must be inactive in the prefab so it does not appear by itself.")]
    [SerializeField] private Button _buttonTemplate;

    [Header("Selection Visuals")]
    [Tooltip("Tint colour applied to the button whose hat is currently equipped.")]
    [SerializeField] private Color _selectedColor = new Color(0.4f, 0.8f, 1f, 1f);

    [Tooltip("Tint colour applied to all other (unselected) buttons.")]
    [SerializeField] private Color _deselectedColor = Color.white;

    [Header("Audio")]
    [Tooltip("Sound played when the player selects a hat button.")]
    [SerializeField] private AudioClip _hatSwapSFX;

    // ─── Private state ────────────────────────────────────────────────────────

    private PlayerHatController _hatController;
    private readonly List<(Button btn, int hatIndex)> _buttons = new();
    private bool _isOpen;

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Populates buttons from <paramref name="hatController"/> and makes the panel visible.
    /// </summary>
    public void Open(PlayerHatController hatController)
    {
        if (_isOpen) return;
        _isOpen = true;

        _hatController = hatController;

        BuildButtons();
        RefreshSelection(_hatController != null ? _hatController.EquippedHatIndex : -1);

        if (_hatController != null)
            _hatController.OnHatChanged += RefreshSelection;

        gameObject.SetActive(true);
    }

    /// <summary>Hides the panel and destroys the dynamically spawned buttons.</summary>
    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_hatController != null)
        {
            _hatController.OnHatChanged -= RefreshSelection;
            _hatController = null;
        }

        gameObject.SetActive(false);
        ClearButtons();
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private void BuildButtons()
    {
        ClearButtons();

        if (_buttonTemplate == null || _buttonContainer == null) return;

        // Index -1 = no hat.
        CreateButton(-1, "No Hat", null);

        if (_hatController != null)
        {
            for (int i = 0; i < _hatController.HatCount; i++)
            {
                HatData data = _hatController.GetHatData(i);
                string label = (data != null && !string.IsNullOrEmpty(data.DisplayName))
                    ? data.DisplayName
                    : $"Hat {i}";
                CreateButton(i, label, data?.PreviewSprite);
            }
        }
    }

    private void CreateButton(int hatIndex, string label, Sprite icon)
    {
        Button btn = Instantiate(_buttonTemplate, _buttonContainer);
        btn.gameObject.SetActive(true);

        // Label text — looks for a TextMeshProUGUI anywhere in the button hierarchy.
        TextMeshProUGUI tmp = btn.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        if (tmp != null)
            tmp.text = label;

        // Optional icon — looks for an Image child named "Icon".
        if (icon != null)
        {
            Image iconImg = btn.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImg != null)
            {
                iconImg.sprite = icon;
                iconImg.enabled = true;
            }
        }

        int capturedIndex = hatIndex;
        btn.onClick.AddListener(() =>
        {
            if (_hatSwapSFX != null && SFXController.Instance != null)
                SFXController.Instance.Play(_hatSwapSFX);

            _hatController?.EquipHat(capturedIndex);
            // Optimistically refresh — RefreshSelection will also fire when the
            // NetworkVariable round-trip completes via OnHatChanged.
            RefreshSelection(capturedIndex);
        });

        _buttons.Add((btn, hatIndex));
    }

    private void RefreshSelection(int equippedIndex)
    {
        foreach (var (btn, hatIndex) in _buttons)
        {
            if (btn == null) continue;

            bool selected = hatIndex == equippedIndex;
            ColorBlock cb = btn.colors;
            cb.normalColor   = selected ? _selectedColor : _deselectedColor;
            cb.selectedColor = cb.normalColor;
            btn.colors = cb;
        }
    }

    private void ClearButtons()
    {
        foreach (var (btn, _) in _buttons)
        {
            if (btn != null)
                Destroy(btn.gameObject);
        }
        _buttons.Clear();
    }
}
