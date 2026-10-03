using System;
using System.Text;
using GoodCopBadCop.Input;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Terminal search bar driven by the virtual canvas cursor. Clicking it focuses it and captures
/// keyboard text (via <see cref="TextInputFocus"/>, so hotkeys are suppressed while typing).
/// Enter / Escape or clicking elsewhere unfocuses it. Every edit raises <see cref="QueryChanged"/>;
/// the owner (PC) replicates the query, then pushes the synced value back with <see cref="SetState"/>.
/// While focused, the locally typed buffer is displayed so typing never waits on a network round trip.
/// </summary>
public sealed class PCSearchField : ClickablePCElement
{
    [Header("Search Field")]
    [SerializeField] private TextMeshProUGUI queryLabel;
    [SerializeField] private TextMeshProUGUI placeholderLabel;
    [Tooltip("Optional object shown only while the field is focused (e.g. a highlight frame).")]
    [SerializeField] private GameObject focusIndicator;
    [Tooltip("Optional button that clears the current query. Hidden while the query is empty.")]
    [SerializeField] private ClickablePCElement clearButton;
    [SerializeField] private string prompt = "> ";
    [SerializeField] private int maxLength = 24;
    [SerializeField] private float caretBlinkInterval = 0.5f;
    [SerializeField] private float backspaceRepeatDelay = 0.4f;
    [SerializeField] private float backspaceRepeatInterval = 0.05f;

    [Header("Typing Audio")]
    [SerializeField] private AudioClip typeSfx;
    [SerializeField, Range(0f, 1f)] private float typeSfxVolume = 0.5f;

    public event Action<string> QueryChanged;

    private readonly StringBuilder _buffer = new();
    private string _syncedQuery = string.Empty;
    private bool _editable;
    private bool _focused;
    private bool _hovered;
    private Keyboard _subscribedKeyboard;
    private float _caretTimer;
    private float _nextBackspaceRepeat;

    public bool IsFocused => _focused;

    protected override void Awake()
    {
        base.Awake();
        if (clearButton != null)
            clearButton.SetClickHandler(ClearQuery);
        RefreshVisuals();
    }

    protected override void OnDisable()
    {
        Blur();
        _hovered = false;
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        Blur();
        base.OnDestroy();
    }

    /// <summary>Applies the replicated query and whether the local player may edit it.</summary>
    public void SetState(string syncedQuery, bool editable)
    {
        _syncedQuery = syncedQuery ?? string.Empty;
        _editable = editable;

        if (!_editable)
            Blur();

        if (!_focused)
        {
            _buffer.Clear();
            _buffer.Append(_syncedQuery);
        }

        RefreshVisuals();
    }

    public override void OnHoverEnter()
    {
        _hovered = true;
        base.OnHoverEnter();
    }

    public override void OnHoverExit()
    {
        _hovered = false;
        base.OnHoverExit();
    }

    public override void OnClick()
    {
        base.OnClick();
        if (_editable)
            Focus();
    }

    private void Focus()
    {
        if (_focused)
            return;

        _focused = true;
        _caretTimer = 0f;
        TextInputFocus.Acquire(this);

        _subscribedKeyboard = Keyboard.current;
        if (_subscribedKeyboard != null)
            _subscribedKeyboard.onTextInput += HandleTextInput;

        RefreshVisuals();
    }

    private void Blur()
    {
        if (_subscribedKeyboard != null)
        {
            _subscribedKeyboard.onTextInput -= HandleTextInput;
            _subscribedKeyboard = null;
        }

        TextInputFocus.Release(this);

        if (!_focused)
            return;

        _focused = false;
        RefreshVisuals();
    }

    private void Update()
    {
        if (!_focused)
            return;

        if (!_editable || !isActiveAndEnabled)
        {
            Blur();
            return;
        }

        // Clicking anywhere on the terminal other than this field (or its clear button) unfocuses it.
        if (UnityEngine.Input.GetMouseButtonDown(0) && !_hovered)
        {
            Blur();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                Blur();
                return;
            }

            HandleBackspace(keyboard);
        }

        _caretTimer += Time.unscaledDeltaTime;
        RefreshVisuals();
    }

    private void HandleBackspace(Keyboard keyboard)
    {
        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            if (keyboard.ctrlKey.isPressed)
                ClearQuery();
            else
                DeleteLastCharacter();

            _nextBackspaceRepeat = Time.unscaledTime + backspaceRepeatDelay;
            return;
        }

        if (keyboard.backspaceKey.isPressed && Time.unscaledTime >= _nextBackspaceRepeat)
        {
            DeleteLastCharacter();
            _nextBackspaceRepeat = Time.unscaledTime + backspaceRepeatInterval;
        }
    }

    private void HandleTextInput(char character)
    {
        if (!_focused || !IsAllowedCharacter(character) || _buffer.Length >= maxLength)
            return;

        // Avoid a leading space; it would never match anything useful.
        if (character == ' ' && _buffer.Length == 0)
            return;

        _buffer.Append(character);
        PlayTypeSfx();
        Commit();
    }

    private static bool IsAllowedCharacter(char character)
    {
        if (char.IsControl(character) || char.IsSurrogate(character))
            return false;

        return char.IsLetterOrDigit(character) || character is ' ' or '-' or '\'' or '.' or ',' or '/';
    }

    private void DeleteLastCharacter()
    {
        if (_buffer.Length == 0)
            return;

        _buffer.Length--;
        PlayTypeSfx();
        Commit();
    }

    private void ClearQuery()
    {
        if (!_editable)
            return;

        bool hadText = _buffer.Length > 0 || _syncedQuery.Length > 0;
        _buffer.Clear();
        if (hadText)
            Commit();
    }

    private void Commit()
    {
        _caretTimer = 0f;
        RefreshVisuals();
        QueryChanged?.Invoke(_buffer.ToString());
    }

    private void RefreshVisuals()
    {
        string text = _focused ? _buffer.ToString() : _syncedQuery;
        bool empty = string.IsNullOrEmpty(text);

        if (queryLabel != null)
        {
            bool caretVisible = _focused && caretBlinkInterval > 0f && Mathf.FloorToInt(_caretTimer / caretBlinkInterval) % 2 == 0;
            string caret = _focused ? (caretVisible ? "_" : " ") : string.Empty;
            queryLabel.text = (empty && !_focused) ? string.Empty : prompt + text + caret;
        }

        if (placeholderLabel != null)
            placeholderLabel.gameObject.SetActive(empty && !_focused);

        if (focusIndicator != null)
            focusIndicator.SetActive(_focused);

        if (clearButton != null)
            clearButton.gameObject.SetActive(_editable && !empty);
    }

    private void PlayTypeSfx()
    {
        if (typeSfx != null)
            SFXController.Instance?.Play(typeSfx, typeSfxVolume);
    }
}
