using UnityEngine;

public class AnomalyManager : MonoBehaviour
{
    public static AnomalyManager Instance;

    private void Awake()
    {
        Instance = this;
    }
}
