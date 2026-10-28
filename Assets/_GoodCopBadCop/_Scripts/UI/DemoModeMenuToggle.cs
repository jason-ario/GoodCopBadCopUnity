using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Swaps main menu content when the build targets the Steam demo
/// (<see cref="GameSettings.IsDemo"/>, i.e. Game Settings > Build Channel = Demo).
/// In demo builds every object in <see cref="_hideInDemo"/> is deactivated, every object in
/// <see cref="_showInDemo"/> is activated, and each <see cref="OpenWebLink"/> in
/// <see cref="_redirectLinks"/> is pointed at <see cref="_demoUrl"/>. Non-demo builds get the
/// inverse visibility and their originally authored URLs.
/// </summary>
public class DemoModeMenuToggle : MonoBehaviour
{
    [Tooltip("Objects shown in Playtest/Release builds and hidden in Demo builds (e.g. survey UI).")]
    [SerializeField] private List<GameObject> _hideInDemo = new();

    [Tooltip("Objects hidden in Playtest/Release builds and shown in Demo builds (e.g. wishlist replacement).")]
    [SerializeField] private List<GameObject> _showInDemo = new();

    [Tooltip("Links whose URL is replaced with Demo Url in Demo builds.")]
    [SerializeField] private List<OpenWebLink> _redirectLinks = new();

    [Tooltip("URL used by Redirect Links in Demo builds.")]
    [SerializeField] private string _demoUrl = "https://store.steampowered.com/app/3262820/Uncanny_Valley_Checkpoint/";

    private readonly Dictionary<OpenWebLink, string> _originalUrls = new();

    private void Awake() => Apply();

    private void OnEnable() => Apply();

    /// <summary>Applies demo or non-demo visibility and link targets from the current Game Settings.</summary>
    public void Apply()
    {
        bool isDemo = GameSettings.Instance.IsDemo;

        foreach (var go in _hideInDemo)
            if (go != null && go.activeSelf == isDemo) go.SetActive(!isDemo);

        foreach (var go in _showInDemo)
            if (go != null && go.activeSelf != isDemo) go.SetActive(isDemo);

        foreach (var link in _redirectLinks)
        {
            if (link == null) continue;
            if (!_originalUrls.ContainsKey(link)) _originalUrls[link] = link.Url;
            link.Url = isDemo && !string.IsNullOrWhiteSpace(_demoUrl) ? _demoUrl : _originalUrls[link];
        }
    }
}
