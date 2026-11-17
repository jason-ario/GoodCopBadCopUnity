using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps UI content based on Game Settings:
/// <list type="bullet">
/// <item><b>Demo lists</b> follow <see cref="GameSettings.IsDemo"/> (Build Channel = Demo), e.g. demo title/body text.</item>
/// <item><b>Survey lists</b> follow <see cref="GameSettings.SurveyEnabled"/>, independent of the channel, so the
/// survey can be shown in Demo builds too.</item>
/// </list>
/// Used on the main menu and on the Thanks For Playing end screen.
/// Re-applies on every enable, so screens that start inactive pick up the current settings when shown.
/// Each <see cref="OpenWebLink"/> in a redirect list is pointed at <see cref="_demoUrl"/> while its condition
/// holds (Demo build / survey disabled), and otherwise keeps its originally authored URL.
/// </summary>
public class DemoModeMenuToggle : MonoBehaviour
{
    [Header("Demo (Build Channel = Demo)")]
    [Tooltip("Objects shown in Playtest/Release builds and hidden in Demo builds.")]
    [SerializeField] private List<GameObject> _hideInDemo = new();

    [Tooltip("Objects hidden in Playtest/Release builds and shown in Demo builds.")]
    [SerializeField] private List<GameObject> _showInDemo = new();

    [Tooltip("Links whose URL is replaced with Demo Url in Demo builds.")]
    [SerializeField] private List<OpenWebLink> _redirectLinks = new();

    [Header("Survey (Game Settings > Survey Enabled)")]
    [Tooltip("Survey UI: shown when Survey Enabled is on, hidden when off.")]
    [SerializeField] private List<GameObject> _surveyObjects = new();

    [Tooltip("Replacement UI (e.g. wishlist label): shown when Survey Enabled is off, hidden when on.")]
    [SerializeField] private List<GameObject> _showWhenSurveyDisabled = new();

    [Tooltip("Links whose URL is replaced with Demo Url when Survey Enabled is off.")]
    [SerializeField] private List<OpenWebLink> _redirectWhenSurveyDisabled = new();

    [Header("Links")]
    [Tooltip("URL used by redirected links (Steam store page).")]
    [SerializeField] private string _demoUrl = "https://store.steampowered.com/app/3262820/Uncanny_Valley_Checkpoint/";

    private readonly Dictionary<OpenWebLink, string> _originalUrls = new();

    private void Awake() => Apply();

    private void OnEnable() => Apply();

    /// <summary>Applies demo and survey visibility and link targets from the current Game Settings.</summary>
    public void Apply()
    {
        var settings = GameSettings.Instance;
        bool isDemo = settings.IsDemo;
        bool surveyEnabled = settings.SurveyEnabled;

        SetActive(_hideInDemo, !isDemo);
        SetActive(_showInDemo, isDemo);
        Redirect(_redirectLinks, isDemo);

        SetActive(_surveyObjects, surveyEnabled);
        SetActive(_showWhenSurveyDisabled, !surveyEnabled);
        Redirect(_redirectWhenSurveyDisabled, !surveyEnabled);
    }

    private static void SetActive(List<GameObject> objects, bool active)
    {
        foreach (var go in objects)
            if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    private void Redirect(List<OpenWebLink> links, bool redirect)
    {
        foreach (var link in links)
        {
            if (link == null) continue;
            if (!_originalUrls.ContainsKey(link)) _originalUrls[link] = link.Url;
            link.Url = redirect && !string.IsNullOrWhiteSpace(_demoUrl) ? _demoUrl : _originalUrls[link];
        }
    }
}
