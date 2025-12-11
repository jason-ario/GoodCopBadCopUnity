using System;
using TMPro;
using UnityEngine;

public class SubtitlesController : MonoBehaviour
{
    private TextMeshProUGUI _textMeshProUGUI;

    private void Awake()
    {
        _textMeshProUGUI = GetComponent<TextMeshProUGUI>();
    }

    public void SetText(string text)
    {
        _textMeshProUGUI.text = text;
    }
}
