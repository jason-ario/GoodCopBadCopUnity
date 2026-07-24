using System;
using System.Collections;
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
    [SerializeField] private GameObject camera;

    private static readonly DateTime StartDate = new DateTime(1989, 10, 20);
    private const string DayNumberKey = "dayNumber";

    private void Awake()
    {
        int day = PlayerPrefs.GetInt(DayNumberKey, 1);
        PopulateFromDay(day);
    }

    private void Start()
    {
        ShiftManager.Instance.OnShiftReady += PopulateNewspaperContents;
        ShiftManager.Instance.OnShiftStart += PopulateNewspaperContents;
    }

    /// <summary>
    /// Refreshes the newspaper using the current day from ShiftManager.
    /// Subscribed to OnShiftReady and OnShiftStart.
    /// </summary>
    public void PopulateNewspaperContents()
    {
        PopulateFromDay(ShiftManager.Instance.CurrentDay);
    }

    /// <summary>
    /// Populates all newspaper text fields for the given day number.
    /// </summary>
    private void PopulateFromDay(int day)
    {
        // Reactivate the hidden content (it deactivates itself again at the end of the snapshot
        // routine below) so its TMP text is actually renderable while the camera captures it.
        gameObject.SetActive(true);

        Debug.Log("Populating Newspaper Contents");
        int index = day - 1;
        string date = StartDate.AddDays(index).ToString("dd MMM yyyy");
        Debug.Log(index);

        NewspaperContentScriptable newspaperContentScriptable = _newspaperContentScriptables[index];
        dateText.text = date;
        headerText.text = newspaperContentScriptable.headerText;
        subheaderText.text = newspaperContentScriptable.subheaderText;
        descriptionText.text = newspaperContentScriptable.descriptionText;
        footerText.text = newspaperContentScriptable.footerText;

        StartCoroutine(CameraSnapshot());
    }

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has been submitted,
    /// then deactivates both the camera and this content root — nothing needs to be active (or
    /// rendered) again until the next populate call reactivates it.
    /// </summary>
    IEnumerator CameraSnapshot()
    {
        yield return new WaitForEndOfFrame();
        camera.SetActive(true);
        yield return new WaitForEndOfFrame();
        camera.SetActive(false);
        gameObject.SetActive(false);
    }
}
