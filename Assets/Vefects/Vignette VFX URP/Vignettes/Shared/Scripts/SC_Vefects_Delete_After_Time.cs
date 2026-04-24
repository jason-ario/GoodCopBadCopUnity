using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SC_Vefects_Delete_After_Time : MonoBehaviour
{

    public float countdownSeconds = 5.0f;

    private void Awake()
    {
        StartCoroutine(waiter());
    }

    IEnumerator waiter()
    {
        yield return new WaitForSeconds(countdownSeconds);
        Object.Destroy(this.gameObject);
    }

}
