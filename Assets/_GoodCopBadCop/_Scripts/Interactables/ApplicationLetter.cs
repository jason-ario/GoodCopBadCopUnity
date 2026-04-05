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
        reasonForEntryText.text =
            suspectData.reasonsForEntry[UnityEngine.Random.Range(0, suspectData.reasonsForEntry.Length)];

        //Change fonts
        nameText.font = suspectData.handwritingFont;
        reasonForEntryText.font = suspectData.handwritingFont;
        birthDateText.font = suspectData.handwritingFont;
        sexText.font = suspectData.handwritingFont;
    }
}
