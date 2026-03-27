using TMPro;
using UnityEngine;

public class EntryPermit : MonoBehaviour
{
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro reasonText;
    [SerializeField] private TextMeshPro expirationDateText;
    [SerializeField] private GameObject seal;

    public void SetEntryPermit(string name, string reason, string expirationDate, bool sealActive)
    {
        nameText.text = "<b>" + name + "</b>";
        reasonText.text = "<b>" + reason + "</b>";
        expirationDateText.text = "<b>" + expirationDate + "</b>";
        seal.SetActive(sealActive);
    }
}
