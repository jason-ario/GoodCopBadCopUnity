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

    /// <summary>Spawns a shop notification with the given message.</summary>
    public void ShowNotification(string message)
    {
        ShopNotification notification = Instantiate(_notificationPrefab, _container);
        notification.Initialize(message);

        if (_notificationSound != null)
            SFXController.Instance.Play(_notificationSound);

        StartCoroutine(DespawnAfterDelay(notification.gameObject));
    }

    private IEnumerator DespawnAfterDelay(GameObject notificationGO)
    {
        yield return new WaitForSeconds(_lifetime);
        Destroy(notificationGO);
    }
}
