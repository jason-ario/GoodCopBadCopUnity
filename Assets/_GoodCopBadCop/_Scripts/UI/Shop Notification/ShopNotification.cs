using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Displays a single shop alert message (e.g. purchase confirmation or error).
/// Attach this to the root of the Shop Notification prefab.
/// </summary>
public class ShopNotification : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _messageText;

    /// <summary>Populates the notification with the given message.</summary>
    public void Initialize(string message)
    {
        _messageText.text = message;
    }

    private void OnDestroy()
    {
        DOTween.Kill(transform);
    }
}
