using System;
using TMPro;
using UnityEngine;

public class NewspaperContentsController : MonoBehaviour
{
    [SerializeField] private TextMeshPro dateText;
    [SerializeField] private TextMeshPro headerText; 
    [SerializeField] TextMeshPro subheaderText; 
    [SerializeField] TextMeshPro descriptionText;
    [SerializeField] TextMeshPro footerText;

    [SerializeField] private NewspaperContentScriptable[] _newspaperContentScriptables;

    private void Start()
    {
        ShiftManager.Instance.OnShiftStart += PopulateNewspaperContents;
    }

    public void PopulateNewspaperContents()
    {
        Debug.Log("Populating Newspaper Contents");
        int index = ShiftManager.Instance.CurrentDay - 1;
        string date = ShiftManager.Instance.CurrentGameDate.ToString("dd MMM yyyy");
        Debug.Log(index);

        NewspaperContentScriptable newspaperContentScriptable = _newspaperContentScriptables[index];
        dateText.text = date;
        headerText.text = newspaperContentScriptable.headerText;
        subheaderText.text = newspaperContentScriptable.subheaderText;
        descriptionText.text = newspaperContentScriptable.descriptionText;
        footerText.text = newspaperContentScriptable.footerText;
    }
}
