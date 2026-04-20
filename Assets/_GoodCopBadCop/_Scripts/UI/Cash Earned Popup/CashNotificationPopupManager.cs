using System.Collections;
using UnityEngine;

public class CashNotificationPopupManager : MonoBehaviour
{
    public CashEarnedNotification cashNotificationPopup;
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

    IEnumerator DespawnAfterTime(GameObject go)
    {
        yield return new WaitForSeconds(lifetime);
        Destroy(go);
    }
}
