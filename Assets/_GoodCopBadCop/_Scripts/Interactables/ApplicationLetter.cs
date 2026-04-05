using HighlightPlus;
using TMPro;
using UnityEngine;

public class ApplicationLetter : Paper
{
    [SerializeField] private TextMeshPro nameText;
    [SerializeField] private TextMeshPro birthDateText;
    [SerializeField] private TextMeshPro sexText;
    [SerializeField] private TextMeshPro reasonForEntryText;

    public void SetInfo(SuspectData suspectData)
    {
        nameText.text = suspectData.FirstName + " " + suspectData.LastName;
        birthDateText.text = suspectData.DateOfBirth;
        sexText.text = suspectData.Sex;
    }
}
