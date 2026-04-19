using TMPro;
using UnityEngine;

public class TerminalListItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    private PC _pc;
    private SuspectData _suspectData;
    
    public void Setup(SuspectData suspectData, PC pc)
    {
        nameText.text = suspectData.LastName + ", " + suspectData.FirstName;
        _suspectData = suspectData;
        _pc = pc;
    }

    public void Select()
    {
        _pc.OpenProfilePage(_suspectData);
    }
}