using UnityEngine;

public class StampContainer : MonoBehaviour
{
    [SerializeField] private GameObject quarantineStamp;
    [SerializeField] private GameObject passStamp;
    [SerializeField] private GameObject killStamp;
    private StampType _stampType;
    public StampType Stamp => _stampType;
    
    public enum StampType
    {
        Quarantine, Pass, Kill
    }
        /// <summary>Activates the correct stamp GameObject and records the stamp type.</summary>
    public void PlaceStamp(StampType stampType)
    {
        _stampType = stampType;

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
