using TMPro;
using UnityEngine;

public class TerminalListItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    private PC _pc;
    private SuspectData _suspectData;
    
    public void Setup(SuspectData suspectData, PC pc, string status = "")
    {
        string displayName = suspectData.LastName + ", " + suspectData.FirstName;
        nameText.text = string.IsNullOrWhiteSpace(status)
            ? displayName
            : displayName + " - " + status;

        _suspectData = suspectData;
        _pc = pc;
    }

    public void Select()
    {
        _pc.OpenProfilePage(_suspectData);
    }
}