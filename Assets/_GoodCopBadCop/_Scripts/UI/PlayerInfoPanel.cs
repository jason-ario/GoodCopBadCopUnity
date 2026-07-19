using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a single player's name, ready state, and host status in the pre-game lobby screen.
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

    /// <summary>Crown icon shown only for the lobby host.</summary>
    [SerializeField] private GameObject hostIcon;

    /// <summary>Populates the panel with a player name, optional ready state, and optional host flag.</summary>
    public void PopulateInfo(string userName, bool isReady = false, bool isHost = false)
    {
        userNameText.text = userName;
        SetReady(isReady);
        SetHost(isHost);
    }

    /// <summary>Updates the ready indicator without changing the player name or host icon.</summary>
    public void SetReady(bool isReady)
    {
        if (readyIndicator == null) return;

        readyIndicator.text  = isReady ? ReadyText : NotReadyText;
        readyIndicator.color = isReady ? ReadyColor : NotReadyColor;
    }

    /// <summary>Shows or hides the host crown icon.
    /// Activation is deferred by one end-of-frame so TMPWidthFitter can finish
    /// resizing the player name before the layout reflows for the icon.</summary>
    public void SetHost(bool isHost)
    {
        if (hostIcon == null) return;

        if (!isHost)
        {
            hostIcon.SetActive(false);
            return;
        }

        StartCoroutine(ActivateHostIconDelayed());
    }

    private IEnumerator ActivateHostIconDelayed()
    {
        yield return new WaitForEndOfFrame();
        if (hostIcon != null)
            hostIcon.SetActive(true);
    }
}
