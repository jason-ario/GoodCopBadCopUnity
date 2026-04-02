using TMPro;
using UnityEngine;

public class TerminalListItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    private PC _pc;
    private SuspectData _suspectData;
    
    public void Setup(SuspectRecord record, PC pc)
    {
        nameText.text = $"{record.Data.FirstName} {record.Data.LastName}";
        _suspectData = record.Data;
        _pc = pc;
    }

    public void Select()
    {
        _pc.OpenProfilePage(_suspectData);
    }
}