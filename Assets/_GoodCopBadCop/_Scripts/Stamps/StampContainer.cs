using UnityEngine;

public class StampContainer : MonoBehaviour
{
    [SerializeField] private GameObject quarantineStamp;
    [SerializeField] private GameObject passStamp;
    [SerializeField] private GameObject killStamp;

    public enum StampType
    {
        Quarantine, Pass, Kill
    }
    
    public void PlaceStamp(StampType stampType)
    {
        switch (stampType)
        {
            case StampType.Pass:
                passStamp.gameObject.SetActive(true);
                break;
            case StampType.Kill:
                killStamp.gameObject.SetActive(true);
                break;
            case StampType.Quarantine:
                quarantineStamp.gameObject.SetActive(true);
                break;
        }
    }
}
