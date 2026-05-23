/// <summary>
/// Every named tutorial gate that CampaignManager can fire during the campaign.
/// Each DayEntry holds a list of steps to trigger at shift start for that day.
/// Add new values here as tutorial moments are designed.
/// </summary>
public enum TutorialStep
{
    None,

    // --- Day 1 ---
    IntroDay1,
    IntroStamping,

    // --- Early days (1–10) ---
    FirstAnomaly,
    UVLightIntro,
    GuidebookIntro,

    // --- Mid days (11–20) ---
    NightTasksExplained,
    NewMechanicUnlock,

    // --- Late days (21–30) ---
    FinalStretch,
    FinalDay,
}
