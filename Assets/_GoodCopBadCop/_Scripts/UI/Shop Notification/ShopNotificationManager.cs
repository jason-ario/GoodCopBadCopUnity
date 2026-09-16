using System.Collections;
using UnityEngine;

/// <summary>
/// Spawns and auto-dismisses shop alert notifications.
/// Assign this to the notification container in the UI Controller prefab.
/// </summary>
public class ShopNotificationManager : MonoBehaviour
{
    [SerializeField] private ShopNotification _notificationPrefab;
    [SerializeField] private Transform _container;
    [SerializeField] private float _lifetime = 2.5f;
    [SerializeField] private AudioClip _notificationSound;
    [Tooltip("Played instead of _notificationSound for confirmed purchases (see ShowPurchaseNotification).")]
    [SerializeField] private AudioClip _purchaseSound;

    /// <summary>Spawns a shop notification with the given message using the default notification sound.</summary>
    public void ShowNotification(string message)
    {
        ShowNotification(message, _notificationSound);
    }

    /// <summary>Spawns a shop notification confirming a purchase, using a distinct purchase sound.</summary>
    public void ShowPurchaseNotification(string message)
    {
        ShowNotification(message, _purchaseSound);
    }

    private void ShowNotification(string message, AudioClip sound)
    {
        ShopNotification notification = Instantiate(_notificationPrefab, _container);
        notification.Initialize(message);

        if (sound != null)
            SFXController.Instance.Play(sound);

        StartCoroutine(DespawnAfterDelay(notification));
    }

    private IEnumerator DespawnAfterDelay(ShopNotification notification)
    {
        yield return new WaitForSeconds(_lifetime);

        if (notification == null)
            yield break;

        notification.FadeOut();
        yield return new WaitForSeconds(notification.FadeOutDuration);

        if (notification != null)
            Destroy(notification.gameObject);
    }
}
