using GameAnalyticsSDK;
using UnityEngine;

public class GAInit : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        GameAnalytics.Initialize();
    }
}
