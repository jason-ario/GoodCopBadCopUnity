using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

/// <summary>
/// Populates the Daily Fax canvas at the start of each day with the newly unlocked anomaly types.
/// Mirrors <see cref="NewspaperContentsController"/> — subscribes to ShiftManager events and
/// triggers a camera snapshot to bake the result into the DailyFax render texture.
/// </summary>
public class DailyFaxContentsController : MonoBehaviour
{
    [SerializeField] private TextMeshPro dateText;
    [SerializeField] private TextMeshPro headerText;
    [SerializeField] private TextMeshPro subheaderText;
    [SerializeField] private TextMeshPro unlockListText;
    [SerializeField] private TextMeshPro footerText;

    [SerializeField] private AnomalyUnlockProgressionSO _unlockProgression;
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
        ShiftManager.Instance.OnShiftReady += PopulateFaxContents;
        ShiftManager.Instance.OnShiftStart += PopulateFaxContents;
    }

    private void OnDestroy()
    {
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftReady -= PopulateFaxContents;
            ShiftManager.Instance.OnShiftStart -= PopulateFaxContents;
        }
    }

    /// <summary>Refreshes the fax using the current day from ShiftManager.</summary>
    public void PopulateFaxContents()
    {
        PopulateFromDay(ShiftManager.Instance.CurrentDay);
    }

    /// <summary>Populates all fax text fields for the given day number.</summary>
    private void PopulateFromDay(int day)
    {
        // Reactivate the hidden content (it deactivates itself again at the end of the snapshot
        // routine below) so its TMP text is actually renderable while the camera captures it.
        gameObject.SetActive(true);

        Debug.Log($"[DailyFaxContentsController] Populating fax for day {day}.");

        int index = day - 1;
        string date = StartDate.AddDays(index).ToString("dd MMM yyyy").ToUpper();
        dateText.text = date;

        headerText.text = "DAILY INTELLIGENCE BRIEFING";
        subheaderText.text = "NEWLY CLASSIFIED THREATS";

        string[] newUnlocks = _unlockProgression != null
            ? _unlockProgression.GetNewUnlocksForDay(day)
            : Array.Empty<string>();

        var sb = new StringBuilder();
        foreach (string typeName in newUnlocks)
            sb.AppendLine($"• {FormatAnomalyName(typeName)}");

        if (sb.Length == 0)
            sb.AppendLine("NO NEW THREATS CLASSIFIED TODAY");

        unlockListText.text = sb.ToString().TrimEnd();
        footerText.text = "CONFIDENTIAL — AUTHORIZED PERSONNEL ONLY";

        StartCoroutine(CameraSnapshot());
    }

    /// <summary>Converts a CamelCase anomaly type name to a human-readable fax entry.</summary>
    private static string FormatAnomalyName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return typeName;
        string stripped = typeName.EndsWith("Anomaly", StringComparison.Ordinal)
            ? typeName[..^"Anomaly".Length]
            : typeName;
        return Regex.Replace(stripped, "(?<!^)([A-Z])", " $1").ToUpper();
    }

    /// <summary>
    /// Activates the render camera for exactly one frame after TMP geometry has been submitted,
    /// then deactivates both the camera and this content root — nothing needs to be active (or
    /// rendered) again until the next populate call reactivates it.
    /// </summary>
    private IEnumerator CameraSnapshot()
    {
        yield return new WaitForEndOfFrame();
        camera.SetActive(true);
        yield return new WaitForEndOfFrame();
        camera.SetActive(false);
        gameObject.SetActive(false);
    }
}
