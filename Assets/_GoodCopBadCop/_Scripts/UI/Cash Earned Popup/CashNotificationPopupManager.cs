using System.Collections;
using UnityEngine;

public class CashNotificationPopupManager : MonoBehaviour
{
    public CashEarnedNotification cashNotificationPopup;
    [Tooltip("Plain white text entry (no panel) used for low-priority messages like player join/leave.")]
    [SerializeField] private TextNotification textNotificationPopup;
    [SerializeField] private Transform container;
    [SerializeField] private float lifetime = 3;
    [SerializeField] AudioClip cashSound;

    public void SpawnCashNotification(int cashAmount, string message)
    {
        CashEarnedNotification cashEarnedNotification = Instantiate(cashNotificationPopup, container);
        cashEarnedNotification.GetComponent<CashEarnedNotification>().Initialize(cashAmount, message);
        SFXController.Instance.Play(cashSound);
        StartCoroutine(DespawnAfterTime(cashEarnedNotification.gameObject));
    }

    /// <summary>
    /// Adds a plain text line to the same top-left stack as the cash popups. No panel, no sound.
    /// </summary>
    public void SpawnTextNotification(string message)
    {
        if (textNotificationPopup == null)
        {
            Debug.LogWarning("[CashNotificationPopupManager] textNotificationPopup is not assigned.");
            return;
        }

        TextNotification notification = Instantiate(textNotificationPopup, container);
        notification.Initialize(message, lifetime);
        StartCoroutine(DespawnAfterTime(notification.gameObject));
    }

    IEnumerator DespawnAfterTime(GameObject go)
    {
        yield return new WaitForSeconds(lifetime);
        Destroy(go);
    }
}
