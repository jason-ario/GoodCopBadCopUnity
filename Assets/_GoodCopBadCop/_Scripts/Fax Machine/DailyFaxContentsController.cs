using System;
using System.Collections;
using System.Globalization;
using System.Text;
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
        Loc.LanguageChanged += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        Loc.LanguageChanged -= OnLanguageChanged;
        if (ShiftManager.Instance != null)
        {
            ShiftManager.Instance.OnShiftReady -= PopulateFaxContents;
            ShiftManager.Instance.OnShiftStart -= PopulateFaxContents;
        }
    }

    /// <summary>Re-bakes the fax so the date follows a language change.</summary>
    private void OnLanguageChanged()
    {
        if (this != null && ShiftManager.Instance != null)
            PopulateFaxContents();
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
        string date = LocalizedDate.Format(StartDate.AddDays(index), LocalizedDate.Style.Named, uppercase: true);
        dateText.text = date;

        headerText.text = Loc.T("DAILY INTELLIGENCE BRIEFING");
        subheaderText.text = Loc.T("NEWLY CLASSIFIED THREATS");

        string[] newUnlocks = _unlockProgression != null
            ? _unlockProgression.GetNewUnlocksForDay(day)
            : Array.Empty<string>();

        var sb = new StringBuilder();
        foreach (string typeName in newUnlocks)
            sb.AppendLine($"• {FormatAnomalyName(typeName)}");

        if (sb.Length == 0)
            sb.AppendLine(Loc.T("NO NEW THREATS CLASSIFIED TODAY"));

        unlockListText.text = sb.ToString().TrimEnd();
        footerText.text = Loc.T("CONFIDENTIAL — AUTHORIZED PERSONNEL ONLY");

        StartCoroutine(CameraSnapshot());
    }

    /// <summary>
    /// Player-facing anomaly name for the fax: the shared <see cref="AnomalyDisplayNames"/> entry (matching the
    /// guidebook), translated through the guidebook's localized display names, then upper-cased in document style.
    /// </summary>
    private static string FormatAnomalyName(string typeName)
    {
        string name = Loc.TranslateSource(AnomalyDisplayNames.Get(typeName));
        return DocumentUpper(name);
    }

    /// <summary>
    /// Upper-cases with the current language's culture. Georgian is left as-is: its Mtavruli capitals are rarely
    /// covered by fonts, and the source text is already caseless in practice.
    /// </summary>
    private static string DocumentUpper(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(Loc.Marker, StringComparison.Ordinal) >= 0) return text;
        string lang = Loc.CurrentLanguage ?? "";
        if (lang.StartsWith("ka", StringComparison.OrdinalIgnoreCase)) return text;
        try { return text.ToUpper(CultureInfo.GetCultureInfo(lang)); }
        catch (CultureNotFoundException) { return text.ToUpperInvariant(); }
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
