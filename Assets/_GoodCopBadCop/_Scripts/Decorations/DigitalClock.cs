using TMPro;
using UnityEngine;

public class DigitalClock : MonoBehaviour
{
    [SerializeField] TMPro.TextMeshPro clockText;
    

    // Update is called once per frame
    void Update()
    {
        clockText.text = TimeSystem.Instance.FormattedTime;
    }
}
