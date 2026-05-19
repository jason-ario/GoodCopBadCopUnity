using TMPro;
using UnityEngine;

/// <summary>
/// Displays a single player's name and ready state in the pre-game lobby screen.
/// </summary>
public class PlayerInfoPanel : MonoBehaviour
{
    private static readonly Color ReadyColor    = new Color(0.18f, 0.72f, 0.18f, 1f);
    private static readonly Color NotReadyColor = new Color(0.85f, 0.15f, 0.15f, 1f);

    private const string ReadyText    = "READY";
    private const string NotReadyText = "NOT READY";

    [SerializeField] private TextMeshProUGUI userNameText;

    /// <summary>TMP label that shows READY / NOT READY with matching colour.</summary>
    [SerializeField] private TextMeshProUGUI readyIndicator;

    /// <summary>Populates the panel with a player name and optional ready state.</summary>
    public void PopulateInfo(string userName, bool isReady = false)
    {
        userNameText.text = userName;
        SetReady(isReady);
    }

    /// <summary>Updates the ready indicator without changing the player name.</summary>
    public void SetReady(bool isReady)
    {
        if (readyIndicator == null) return;

        readyIndicator.text  = isReady ? ReadyText : NotReadyText;
        readyIndicator.color = isReady ? ReadyColor : NotReadyColor;
    }
}
